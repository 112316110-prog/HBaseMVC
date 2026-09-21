namespace HBaseMVC.Models
{
    public class MedicalRecordSearchItem
    {
        // 病人資料
        public string RowKey { get; set; } = "";
        public string PatientId { get; set; } = "";
        public string MedicalRecordNumber { get; set; } = "";
        public string PatientName { get; set; } = "";
        public string Gender { get; set; } = "";
        public string BirthDate { get; set; } = "";

        // 單筆病歷資料
        public string RecordId { get; set; } = "";
        public string OccurredAt { get; set; } = "";
        public string Diagnosis { get; set; } = "";
        public string BodyPart { get; set; } = "";
        public string Finding { get; set; } = "";
        public string Treatment { get; set; } = "";
        public string Physician { get; set; } = "";
        public string Note { get; set; } = "";
        public string ResourceType { get; set; } = "";
        public bool IsDissected { get; set; } = false;
    }
}