namespace HBaseMVC.Models
{
    public class MedicalRecord
    {
        public string Id { get; set; } = "";
        public string ResourceType { get; set; } = "Observation";
        public string OccurredAt { get; set; } = "";
        public string Diagnosis { get; set; } = "";
        public string BodyPart { get; set; } = "";
        public string Finding { get; set; } = "";
        public string Treatment { get; set; } = "";
        public string Physician { get; set; } = "";
        public string Note { get; set; } = "";
        public string FhirJson { get; set; } = "";
    }
}
