using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Observability;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/messages")]
[RequireAuth]
[RequireModule("messaging")]
[EnableRateLimiting("GeneralApiPolicy")]
public class MessagesController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;
    private readonly IPortalNotificationService _notifications;

    public MessagesController(MedicalPortalDbContext db, IAuditService audit, IPortalNotificationService notifications)
    {
        _db = db;
        _audit = audit;
        _notifications = notifications;
    }

    private Guid TargetPatientId => (Guid)HttpContext.Items["TargetPatientId"]!;
    private AccessTokenClaims Actor => (AccessTokenClaims)HttpContext.Items["Actor"]!;

    [HttpGet]
    [RequirePatientAccess(Permission.message, AppResource.Messages, AppAction.Read)]
    public async Task<IActionResult> ListThreads()
    {
        var threads = await _db.MessageThreads
            .AsNoTracking()
            .Include(t => t.Participants).ThenInclude(p => p.Person)
            .Include(t => t.Messages).ThenInclude(m => m.Sender)
            .Where(t => t.PatientId == TargetPatientId)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync();

        var threadDtos = threads.Select(t =>
        {
            var lastMsg = t.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault();
            var participant = t.Participants.FirstOrDefault(p => p.PersonId == Actor.PersonId);
            var unread = lastMsg != null && (participant?.LastReadMessageId == null || participant.LastReadMessageId != lastMsg.Id);

            return new
            {
                id = t.Id,
                subject = t.Subject,
                status = t.Status,
                lastMessageAt = lastMsg?.SentAt.ToString("o"),
                unread,
                preview = lastMsg?.Body != null ? (lastMsg.Body.Length > 80 ? lastMsg.Body[..80] + "…" : lastMsg.Body) : null,
                participants = t.Participants.Select(p => new
                {
                    name = $"{p.Person.FirstName} {p.Person.LastName}",
                    role = p.Person.Accounts.FirstOrDefault()?.Role.ToString() ?? "PATIENT"
                })
            };
        }).ToList();

        return Ok(new
        {
            threads = threadDtos,
            unreadCount = threadDtos.Count(t => t.unread)
        });
    }

    [HttpGet("care-team")]
    [RequirePatientAccess(Permission.message, AppResource.Messages, AppAction.Read)]
    public async Task<IActionResult> GetCareTeam()
    {
        var team = await _db.CareTeams
            .AsNoTracking()
            .Include(ct => ct.Provider)
            .Where(ct => ct.PatientId == TargetPatientId && ct.Provider.AccountId != null)
            .OrderByDescending(ct => ct.IsPrimary)
            .ToListAsync();

        return Ok(new
        {
            careTeam = team.Select(t => new
            {
                providerId = t.Provider.Id,
                name = $"Dr. {t.Provider.FirstName} {t.Provider.LastName}",
                credentials = t.Provider.Credentials,
                specialty = t.Provider.Specialty,
                isPrimary = t.IsPrimary
            })
        });
    }

    public record StartThreadBody(string ProviderId, string Subject, string Body, string? Category);

    [HttpPost]
    [RequirePatientAccess(Permission.message, AppResource.Messages, AppAction.Write)]
    public async Task<IActionResult> StartThread([FromBody] StartThreadBody body)
    {
        if (string.IsNullOrWhiteSpace(body.Subject) || string.IsNullOrWhiteSpace(body.Body))
            return BadRequest(new { error = "Subject and message body are required." });

        if (!Guid.TryParse(body.ProviderId, out var providerId))
            return BadRequest(new { error = "Valid provider ID is required." });

        var provider = await _db.Providers
            .Include(p => p.Account).ThenInclude(a => a!.Person)
            .FirstOrDefaultAsync(p => p.Id == providerId);

        if (provider?.Account?.Person == null)
            return BadRequest(new { error = "Provider cannot receive messages." });

        var thread = new MessageThread
        {
            PatientId = TargetPatientId,
            Subject = body.Subject.Trim(),
            Category = body.Category ?? "General",
            Status = "OPEN",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.MessageThreads.Add(thread);

        var firstMsg = new Message
        {
            Thread = thread,
            SenderPersonId = Actor.PersonId,
            Body = body.Body.Trim(),
            SentAt = DateTime.UtcNow
        };
        _db.Messages.Add(firstMsg);

        // Participants
        _db.MessageThreadParticipants.Add(new MessageThreadParticipant
        {
            Thread = thread,
            PersonId = Actor.PersonId,
            LastReadMessageId = firstMsg.Id
        });

        _db.MessageThreadParticipants.Add(new MessageThreadParticipant
        {
            Thread = thread,
            PersonId = provider.Account.Person.Id,
            LastReadMessageId = null
        });

        await _db.SaveChangesAsync();

        using (var activity = MedicalPortalDiagnostics.ActivitySource.StartActivity("portal.messages.send"))
        {
            activity?.SetTag("thread.id", thread.Id.ToString());
            MedicalPortalDiagnostics.MessagesSentTotal.Add(1);
        }

        await _audit.LogAsync("MESSAGE_SENT", "MessageThread", thread.Id.ToString(), Actor.UserAccountId, TargetPatientId);
        await _notifications.NotifyNewMessageAsync(thread.Id, provider.Account.Person.Id, new
        {
            threadId = thread.Id,
            messageId = firstMsg.Id,
            senderPersonId = Actor.PersonId,
            body = firstMsg.Body,
            sentAt = firstMsg.SentAt.ToString("o")
        }, TargetPatientId);

        return StatusCode(201, new
        {
            thread = new
            {
                id = thread.Id,
                subject = thread.Subject,
                status = thread.Status,
                createdAt = thread.CreatedAt.ToString("o")
            }
        });
    }

    [HttpGet("{id}")]
    [RequirePatientAccess(Permission.message, AppResource.Messages, AppAction.Read)]
    public async Task<IActionResult> GetThread(Guid id)
    {
        var thread = await _db.MessageThreads
            .Include(t => t.Participants)
            .Include(t => t.Messages).ThenInclude(m => m.Sender).ThenInclude(s => s.Accounts)
            .FirstOrDefaultAsync(t => t.Id == id && t.PatientId == TargetPatientId);

        if (thread == null)
            return NotFound(new { error = "Thread not found." });

        // Mark as read for this actor
        var participant = thread.Participants.FirstOrDefault(p => p.PersonId == Actor.PersonId);
        var lastMsg = thread.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault();
        if (participant != null && lastMsg != null && participant.LastReadMessageId != lastMsg.Id)
        {
            participant.LastReadMessageId = lastMsg.Id;
            await _db.SaveChangesAsync();
        }

        return Ok(new
        {
            thread = new
            {
                id = thread.Id,
                subject = thread.Subject,
                status = thread.Status,
                createdAt = thread.CreatedAt.ToString("o"),
                messages = thread.Messages.OrderBy(m => m.SentAt).Select(m => new
                {
                    id = m.Id,
                    body = m.Body,
                    sentAt = m.SentAt.ToString("o"),
                    mine = m.SenderPersonId == Actor.PersonId,
                    senderName = $"{m.Sender.FirstName} {m.Sender.LastName}",
                    senderRole = m.Sender.Accounts.FirstOrDefault()?.Role.ToString() ?? "PATIENT",
                    deleted = m.Deleted
                })
            }
        });
    }

    public record ReplyBody(string Body);

    [HttpPost("{id}/replies")]
    [RequirePatientAccess(Permission.message, AppResource.Messages, AppAction.Write)]
    public async Task<IActionResult> ReplyToThread(Guid id, [FromBody] ReplyBody body)
    {
        if (string.IsNullOrWhiteSpace(body.Body))
            return BadRequest(new { error = "Message text is required." });

        var thread = await _db.MessageThreads
            .Include(t => t.Participants)
            .FirstOrDefaultAsync(t => t.Id == id && t.PatientId == TargetPatientId);

        if (thread == null)
            return NotFound(new { error = "Thread not found." });

        var message = new Message
        {
            ThreadId = thread.Id,
            SenderPersonId = Actor.PersonId,
            Body = body.Body.Trim(),
            SentAt = DateTime.UtcNow
        };
        _db.Messages.Add(message);

        thread.UpdatedAt = DateTime.UtcNow;

        var participant = thread.Participants.FirstOrDefault(p => p.PersonId == Actor.PersonId);
        if (participant != null)
        {
            participant.LastReadMessageId = message.Id;
        }

        await _db.SaveChangesAsync();

        await _audit.LogAsync("MESSAGE_SENT", "Message", message.Id.ToString(), Actor.UserAccountId, TargetPatientId);
        await _notifications.NotifyNewMessageAsync(thread.Id, Actor.PersonId, new
        {
            threadId = thread.Id,
            messageId = message.Id,
            senderPersonId = Actor.PersonId,
            body = message.Body,
            sentAt = message.SentAt.ToString("o")
        }, TargetPatientId);

        return StatusCode(201, new
        {
            message = new
            {
                id = message.Id,
                body = message.Body,
                sentAt = message.SentAt.ToString("o")
            }
        });
    }
}
