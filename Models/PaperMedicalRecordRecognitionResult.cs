namespace HBaseMVC.Models
{
    public class PaperMedicalRecordRecognitionResult
    {
        public string Row { get; set; } = "";
        public string MedicalRecordNumber { get; set; } = "";
        public string PatientName { get; set; } = "";

        public string SourceFileName { get; set; } = "";
        public string FullRawText { get; set; } = "";

        public List<PaperMedicalRecordEntry> Records { get; set; } = new();

        public List<string> GlobalWarnings { get; set; } = new();
    }

    public class PaperMedicalRecordEntry
    {
        public string RecordDate { get; set; } = "";
        public string RecordTime { get; set; } = "";

        public string RawText { get; set; } = "";

        public string Symptoms { get; set; } = "";
        public string Assessment { get; set; } = "";
        public string Treatment { get; set; } = "";
        public string NursingNote { get; set; } = "";

        public string BloodPressure { get; set; } = "";
        public string Pulse { get; set; } = "";
        public string RespiratoryRate { get; set; } = "";
        public string Temperature { get; set; } = "";
        public string OxygenSaturation { get; set; } = "";
        public string BodyWeight { get; set; } = "";

        public List<string> Medications { get; set; } = new();
        public List<string> LabFindings { get; set; } = new();

        public double Confidence { get; set; }

        public List<string> Warnings { get; set; } = new();
    }
}