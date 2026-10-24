using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MedicalPortal.Web.Filters;

public class PhiSwaggerHygieneFilter : ISchemaFilter, IOperationFilter
{
    private static readonly Dictionary<string, string> SensitivePlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        { "mrn", "MRN-000000" },
        { "patientMrn", "MRN-000000" },
        { "hmsMrn", "HMS-000000" },
        { "ssn", "000-00-0000" },
        { "dateOfBirth", "1970-01-01" },
        { "dob", "1970-01-01" },
        { "firstName", "Jane" },
        { "lastName", "Doe" },
        { "patientName", "Jane Doe" },
        { "phone", "+251 11 661 0100" },
        { "emergencyContactPhone", "+251 11 661 0101" },
        { "emergencyContactName", "Synthetic Contact" },
        { "addressLine1", "Bole Subcity, Woreda 03, House 100" },
        { "addressLine2", "Suite 100" },
        { "notes", "[SYNTHETIC_CLINICAL_NOTE]" },
        { "patientNotes", "[SYNTHETIC_PATIENT_NOTE]" },
        { "reasonForVisit", "Routine wellness examination" },
        { "clinicalResponse", "[SYNTHETIC_CLINICAL_RESPONSE]" },
        { "answersJson", "{\"question_1\":\"synthetic_response\"}" },
        { "dataJson", "{\"synthetic\":\"payload\"}" }
    };

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties == null) return;

        foreach (var property in schema.Properties)
        {
            if (SensitivePlaceholders.TryGetValue(property.Key, out var placeholder))
            {
                property.Value.Example = new OpenApiString(placeholder);
                if (string.IsNullOrEmpty(property.Value.Description))
                {
                    property.Value.Description = "[PHI: Synthetic placeholder. Never submit real patient data.]";
                }
            }
        }
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Parameters == null) return;

        foreach (var param in operation.Parameters)
        {
            if (param.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                param.Description = (param.Description ?? "") + " (Provider / clinician search only. Never query patient names in URLs.)";
            }
        }
    }
}
