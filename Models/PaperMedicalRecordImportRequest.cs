using System.Collections.Generic;

namespace HBaseMVC.Models
{
    public class PaperMedicalRecordImportRequest
    {
        public string Row { get; set; } = "";
        public string MedicalRecordNumber { get; set; } = "";
        public string PatientName { get; set; } = "";
        public string SourceFileName { get; set; } = "";
        public string FullRawText { get; set; } = "";

        public List<PaperMedicalRecordEntry> Records { get; set; } = new();
        public List<string> GlobalWarnings { get; set; } = new();
    }
}