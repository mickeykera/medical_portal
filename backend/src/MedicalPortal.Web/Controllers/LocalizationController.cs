using MedicalPortal.Core.Common;
using MedicalPortal.Infrastructure.Localization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/localization")]
public class LocalizationController : ControllerBase
{
    private readonly ILocalizationService _localization;

    public LocalizationController(ILocalizationService localization)
    {
        _localization = localization;
    }

    /// <summary>
    /// Returns the full translation dictionary for the specified language (en, am, om).
    /// </summary>
    [HttpGet("{language}")]
    public IActionResult GetDictionary(string language)
    {
        var normalized = language?.Trim().ToLowerInvariant() ?? "en";
        var dict = _localization.GetCatalog(normalized);
        return Ok(new
        {
            language = normalized,
            strings = dict
        });
    }

    /// <summary>
    /// Returns supported languages metadata (English, Amharic, Afaan Oromoo).
    /// </summary>
    [HttpGet("languages")]
    public IActionResult GetSupportedLanguages()
    {
        return Ok(_localization.GetSupportedLanguages());
    }
}
