using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/inventory")]
[RequireAuth]
public class InventoryController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;

    public InventoryController(MedicalPortalDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet("items")]
    [RequirePermission(AppResource.Medications, AppAction.Read)]
    public async Task<IActionResult> Items()
    {
        var items = await _db.InventoryItems.AsNoTracking().Where(i => i.IsActive).OrderBy(i => i.GenericName).ToListAsync();
        return Ok(new { items });
    }

    [HttpGet("stores")]
    [RequirePermission(AppResource.Medications, AppAction.Read)]
    public async Task<IActionResult> Stores()
    {
        var stores = await _db.InventoryStores.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        return Ok(new { stores });
    }

    [HttpGet("stock")]
    [RequirePermission(AppResource.Medications, AppAction.Read)]
    public async Task<IActionResult> Stock([FromQuery] Guid storeId)
    {
        var rows = await _db.StockLedgerEntries.AsNoTracking()
            .Where(e => e.StoreId == storeId)
            .GroupBy(e => new { e.ItemId, e.BatchId })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.BatchId,
                Quantity = g.Sum(e => e.Action == StockLedgerAction.RECEIVE ||
                    e.Action == StockLedgerAction.RETURN ? e.Quantity : -e.Quantity)
            })
            .Where(x => x.Quantity > 0)
            .Join(_db.InventoryItems, x => x.ItemId, i => i.Id, (x, i) => new { x, i })
            .Join(_db.StockBatches, x => x.x.BatchId, b => b.Id, (x, b) => new
            {
                itemId = x.x.ItemId,
                item = x.i.GenericName,
                batchId = x.x.BatchId,
                lotNumber = b.LotNumber,
                expiryDate = b.ExpiryDate,
                quantity = x.x.Quantity,
                quarantined = b.IsQuarantined
            })
            .OrderBy(x => x.expiryDate)
            .ToListAsync();
        return Ok(new { storeId, stock = rows });
    }

    [HttpPost("receive")]
    [RequirePermission(AppResource.Medications, AppAction.Write)]
    public async Task<IActionResult> Receive([FromBody] ReceiveRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (request.Quantity <= 0) return BadRequest(new { error = "Quantity must be positive." });
        if (!await _db.InventoryStores.AnyAsync(s => s.Id == request.StoreId && s.IsActive))
            return NotFound(new { error = "Store not found." });
        if (!await _db.InventoryItems.AnyAsync(i => i.Id == request.ItemId && i.IsActive))
            return NotFound(new { error = "Item not found." });

        var batch = await _db.StockBatches.FirstOrDefaultAsync(b => b.ItemId == request.ItemId && b.LotNumber == request.LotNumber);
        if (batch == null)
        {
            batch = new StockBatch { ItemId = request.ItemId, LotNumber = request.LotNumber.Trim(), ExpiryDate = request.ExpiryDate, Manufacturer = request.Manufacturer };
            _db.StockBatches.Add(batch);
        }
        _db.StockLedgerEntries.Add(new StockLedgerEntry
        {
            StoreId = request.StoreId, ItemId = request.ItemId, Batch = batch,
            Action = StockLedgerAction.RECEIVE, Quantity = request.Quantity,
            ActorUserAccountId = actor.UserAccountId, Reference = request.Reference?.Trim()
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("STOCK_RECEIVED", "StockLedgerEntry", null, actor.UserAccountId,
            metadata: new { request.ItemId, request.StoreId, request.Quantity, request.LotNumber });
        return Ok(new { itemId = request.ItemId, storeId = request.StoreId, quantity = request.Quantity });
    }

    [HttpPost("issue")]
    [RequirePermission(AppResource.Medications, AppAction.Write)]
    public async Task<IActionResult> Issue([FromBody] IssueRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (request.Quantity <= 0) return BadRequest(new { error = "Quantity must be positive." });
        if (request.Action is not (StockLedgerAction.ISSUE or StockLedgerAction.DISPENSE or StockLedgerAction.CONSUME or StockLedgerAction.WASTAGE or StockLedgerAction.EXPIRY_WRITE_OFF))
            return BadRequest(new { error = "Invalid issue ledger action." });
        var transaction = _db.Database.CurrentTransaction;
        var ownsTransaction = transaction == null;
        if (transaction == null)
            transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var batches = await _db.StockBatches.AsNoTracking()
            .Where(b => b.ItemId == request.ItemId && !b.IsQuarantined &&
                (!b.ExpiryDate.HasValue || b.ExpiryDate > DateTime.UtcNow))
            .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
            .ToListAsync();
        var balances = await _db.StockLedgerEntries.AsNoTracking()
            .Where(e => e.StoreId == request.StoreId && e.ItemId == request.ItemId)
            .GroupBy(e => e.BatchId)
            .Select(g => new { BatchId = g.Key, Quantity = g.Sum(e => e.Action == StockLedgerAction.RECEIVE || e.Action == StockLedgerAction.RETURN ? e.Quantity : -e.Quantity) })
            .ToDictionaryAsync(x => x.BatchId, x => x.Quantity);
        var remaining = request.Quantity;
        var selected = batches.Where(b => balances.GetValueOrDefault(b.Id) > 0).Select(b => new { Batch = b, Available = balances[b.Id] }).ToList();
        if (selected.Sum(x => x.Available) < request.Quantity)
            return Conflict(new { error = "Insufficient available stock." });
        foreach (var entry in selected)
        {
            if (remaining <= 0) break;
            var quantity = Math.Min(remaining, entry.Available);
            _db.StockLedgerEntries.Add(new StockLedgerEntry
            {
                StoreId = request.StoreId, ItemId = request.ItemId, BatchId = entry.Batch.Id,
                Action = request.Action, Quantity = quantity, PatientId = request.PatientId,
                EncounterId = request.EncounterId, ActorUserAccountId = actor.UserAccountId, Reason = request.Reason?.Trim()
            });
            remaining -= quantity;
        }
        await _db.SaveChangesAsync();
        if (ownsTransaction)
        {
            await transaction.CommitAsync();
            await transaction.DisposeAsync();
        }
        await _audit.LogAsync("STOCK_ISSUED", "StockLedgerEntry", null, actor.UserAccountId, request.PatientId,
            metadata: new { request.ItemId, request.StoreId, request.Quantity, request.Action });
        return Ok(new { itemId = request.ItemId, storeId = request.StoreId, quantity = request.Quantity });
    }

    [HttpGet("analytics")]
    [RequirePermission(AppResource.Medications, AppAction.Read)]
    public async Task<IActionResult> Analytics([FromQuery] Guid storeId)
    {
        var balances = await _db.StockLedgerEntries.AsNoTracking().Where(e => e.StoreId == storeId)
            .GroupBy(e => e.ItemId)
            .Select(g => new
            {
                itemId = g.Key,
                quantity = g.Sum(e => e.Action == StockLedgerAction.RECEIVE || e.Action == StockLedgerAction.RETURN ? e.Quantity : -e.Quantity)
            }).ToListAsync();
        var items = await _db.InventoryItems.AsNoTracking().Where(i => balances.Select(x => x.itemId).Contains(i.Id))
            .ToDictionaryAsync(i => i.Id);
        return Ok(new
        {
            storeId,
            stock = balances.Select(x => new
            {
                x.itemId,
                item = items[x.itemId].GenericName,
                x.quantity,
                minimumLevel = items[x.itemId].MinimumLevel,
                maximumLevel = items[x.itemId].MaximumLevel,
                status = x.quantity <= 0 ? "STOCK_OUT" : x.quantity < items[x.itemId].MinimumLevel ? "REORDER" : "IN_RANGE",
                venClass = items[x.itemId].VenClass
            })
        });
    }

    [HttpPost("requisitions")]
    [RequirePermission(AppResource.Medications, AppAction.Write)]
    public async Task<IActionResult> CreateRequisition([FromBody] CreateRequisitionRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (request.Lines.Count == 0 || request.Lines.Any(x => x.Quantity <= 0 || x.ItemId == Guid.Empty))
            return BadRequest(new { error = "At least one item with a positive quantity is required." });
        if (!await _db.InventoryStores.AnyAsync(s => s.Id == request.FromStoreId && s.IsActive) ||
            !await _db.InventoryStores.AnyAsync(s => s.Id == request.ToStoreId && s.IsActive))
            return NotFound(new { error = "Source or destination store not found." });
        var requisition = new InventoryRequisition
        {
            FromStoreId = request.FromStoreId, ToStoreId = request.ToStoreId,
            RequestedByUserAccountId = actor.UserAccountId, Reason = request.Reason?.Trim(),
            Lines = request.Lines.Select(x => new InventoryRequisitionLine { ItemId = x.ItemId, Quantity = x.Quantity }).ToList()
        };
        _db.InventoryRequisitions.Add(requisition);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("INVENTORY_REQUISITION_CREATED", "InventoryRequisition", requisition.Id.ToString(), actor.UserAccountId);
        return Ok(new { requisitionId = requisition.Id, status = requisition.Status.ToString() });
    }

    [HttpPost("requisitions/{id:guid}/approve")]
    [RequirePermission(AppResource.Medications, AppAction.Write)]
    public async Task<IActionResult> ApproveRequisition(Guid id)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        var requisition = await _db.InventoryRequisitions.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (requisition == null) return NotFound(new { error = "Requisition not found." });
        if (requisition.Status != InventoryRequisitionStatus.DRAFT)
            return Conflict(new { error = "Only draft requisitions can be approved." });
        if (requisition.RequestedByUserAccountId == actor.UserAccountId)
            return Forbid();
        requisition.Status = InventoryRequisitionStatus.APPROVED;
        requisition.ApprovedByUserAccountId = actor.UserAccountId;
        requisition.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("INVENTORY_REQUISITION_APPROVED", "InventoryRequisition", id.ToString(), actor.UserAccountId);
        return Ok(new { requisitionId = id, status = requisition.Status.ToString() });
    }

    [HttpPost("controlled-drugs")]
    [RequirePermission(AppResource.Medications, AppAction.Write)]
    public async Task<IActionResult> RecordControlledDrug([FromBody] ControlledDrugRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (request.Quantity <= 0 || request.PatientId == Guid.Empty || request.SecondSignatureUserAccountId == actor.UserAccountId)
            return BadRequest(new { error = "Positive quantity, patient attribution, and a distinct second signature are required." });
        var batch = await _db.StockBatches.FirstOrDefaultAsync(x => x.Id == request.BatchId && x.ItemId == request.ItemId);
        if (batch == null) return NotFound(new { error = "Batch not found." });
        var entry = new ControlledDrugEntry
        {
            StoreId = request.StoreId, ItemId = request.ItemId, BatchId = request.BatchId,
            Quantity = request.Quantity, PatientId = request.PatientId, EncounterId = request.EncounterId,
            FirstSignatureUserAccountId = actor.UserAccountId, SecondSignatureUserAccountId = request.SecondSignatureUserAccountId,
            Reason = request.Reason.Trim()
        };
        _db.ControlledDrugEntries.Add(entry);
        _db.StockLedgerEntries.Add(new StockLedgerEntry
        {
            StoreId = request.StoreId, ItemId = request.ItemId, BatchId = request.BatchId,
            Action = StockLedgerAction.DISPENSE, Quantity = request.Quantity, PatientId = request.PatientId,
            EncounterId = request.EncounterId, ActorUserAccountId = actor.UserAccountId, Reason = request.Reason.Trim()
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CONTROLLED_DRUG_DUAL_SIGNED", "ControlledDrugEntry", entry.Id.ToString(), actor.UserAccountId, request.PatientId);
        return Ok(new { entryId = entry.Id });
    }

    [HttpPost("counts")]
    [RequirePermission(AppResource.Medications, AppAction.Write)]
    public async Task<IActionResult> CreateCountSheet([FromBody] CreateCountSheetRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (!await _db.InventoryStores.AnyAsync(x => x.Id == request.StoreId && x.IsActive))
            return NotFound(new { error = "Store not found." });
        if (request.Lines.Count == 0 || request.Lines.Any(x => x.CountedQuantity < 0))
            return BadRequest(new { error = "Count lines and non-negative counted quantities are required." });
        var sheet = new InventoryCountSheet
        {
            StoreId = request.StoreId,
            CreatedByUserAccountId = actor.UserAccountId,
            Lines = request.Lines.Select(x => new InventoryCountLine
            {
                ItemId = x.ItemId, BatchId = x.BatchId, CountedQuantity = x.CountedQuantity,
                SystemQuantity = 0, VarianceReason = x.VarianceReason?.Trim()
            }).ToList()
        };
        foreach (var line in sheet.Lines)
        {
            line.SystemQuantity = await CurrentBatchQuantity(request.StoreId, line.ItemId, line.BatchId);
        }
        _db.InventoryCountSheets.Add(sheet);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("INVENTORY_COUNT_CREATED", "InventoryCountSheet", sheet.Id.ToString(), actor.UserAccountId);
        return Ok(new { countSheetId = sheet.Id, status = "PENDING_APPROVAL" });
    }

    [HttpPost("counts/{id:guid}/approve")]
    [RequirePermission(AppResource.Medications, AppAction.Write)]
    public async Task<IActionResult> ApproveCountSheet(Guid id)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        var sheet = await _db.InventoryCountSheets.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (sheet == null) return NotFound(new { error = "Count sheet not found." });
        if (sheet.IsApproved) return Conflict(new { error = "Count sheet is already approved." });
        if (sheet.CreatedByUserAccountId == actor.UserAccountId) return Forbid();
        foreach (var line in sheet.Lines)
        {
            var variance = line.CountedQuantity - line.SystemQuantity;
            if (variance == 0) continue;
            _db.StockLedgerEntries.Add(new StockLedgerEntry
            {
                StoreId = sheet.StoreId, ItemId = line.ItemId, BatchId = line.BatchId,
                Action = variance > 0 ? StockLedgerAction.RECEIVE : StockLedgerAction.ADJUSTMENT,
                Quantity = Math.Abs(variance), ActorUserAccountId = actor.UserAccountId,
                Reference = $"COUNT:{sheet.Id}", Reason = line.VarianceReason
            });
        }
        sheet.IsApproved = true;
        sheet.ApprovedByUserAccountId = actor.UserAccountId;
        sheet.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("INVENTORY_COUNT_APPROVED", "InventoryCountSheet", id.ToString(), actor.UserAccountId);
        return Ok(new { countSheetId = id, status = "APPROVED" });
    }

    private async Task<int> CurrentBatchQuantity(Guid storeId, Guid itemId, Guid batchId)
    {
        return await _db.StockLedgerEntries.Where(x => x.StoreId == storeId && x.ItemId == itemId && x.BatchId == batchId)
            .SumAsync(x => x.Action == StockLedgerAction.RECEIVE || x.Action == StockLedgerAction.RETURN ? x.Quantity : -x.Quantity);
    }

    public sealed record ReceiveRequest(Guid StoreId, Guid ItemId, string LotNumber, int Quantity, DateTime? ExpiryDate = null, string? Manufacturer = null, string? Reference = null);
    public sealed record IssueRequest(Guid StoreId, Guid ItemId, int Quantity, StockLedgerAction Action = StockLedgerAction.ISSUE, Guid? PatientId = null, Guid? EncounterId = null, string? Reason = null);
    public sealed record RequisitionLineRequest(Guid ItemId, int Quantity);
    public sealed record CreateRequisitionRequest(Guid FromStoreId, Guid ToStoreId, List<RequisitionLineRequest> Lines, string? Reason = null);
    public sealed record ControlledDrugRequest(Guid StoreId, Guid ItemId, Guid BatchId, int Quantity, Guid PatientId, Guid? EncounterId, Guid SecondSignatureUserAccountId, string Reason);
    public sealed record CountLineRequest(Guid ItemId, Guid BatchId, int CountedQuantity, string? VarianceReason = null);
    public sealed record CreateCountSheetRequest(Guid StoreId, List<CountLineRequest> Lines);
}
