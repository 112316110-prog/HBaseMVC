namespace HBaseMVC.Models
{
    public class LabTestRecord
    {
        public string Id { get; set; } = "";

        public string TestDate { get; set; } = "";

        public string TestItem { get; set; } = "";

        public string TestResult { get; set; } = "";

        public string TestItemCode { get; set; } = "";

        public string TestItemName { get; set; } = "";

        public string ResultType { get; set; } = "";

        public string NumericResult { get; set; } = "";

        public string TextResult { get; set; } = "";

        public string ImageResultPath { get; set; } = "";

        public string WaveformResultPath { get; set; } = "";

        public string AudioResultPath { get; set; } = "";

        public string ExaminationType { get; set; } = "";

        public string BodyPart { get; set; } = "";

        public string Interpretation { get; set; } = "";

        public string DicomResultPath { get; set; } = "";

        public string VideoResultPath { get; set; } = "";

        public string ReportFilePath { get; set; } = "";

        public string Unit { get; set; } = "";

        public string ReferenceRange { get; set; } = "";

        public string AbnormalFlag { get; set; } = "";

        public string TestMethod { get; set; } = "";

        public string SpecimenCollectedAt { get; set; } = "";
    }
}