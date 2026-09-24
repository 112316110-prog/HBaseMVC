namespace HBaseMVC.Models
{
    public class TeachingApplication
    {
        public string RowKey { get; set; } = "";

        public string Applicant { get; set; } = "";

        public string GroupName { get; set; } = "";

        public string ClassTime { get; set; } = "";

        public string Age { get; set; } = "";

        public string MedicalHistory { get; set; } = "";

        public string Diagnosis { get; set; } = "";

        public string Department { get; set; } = "";

        public string Assistant { get; set; } = "";

        public string MainDoctor { get; set; } = "";

        public string Nurse { get; set; } = "";

        public string CourseContent { get; set; } = "";

        public string SurgeryTopic { get; set; } = "";

        public string Procedure { get; set; } = "";

        public string BodyPart { get; set; } = "";

        public string CreatedAt { get; set; } = "";

        public string Status { get; set; } = "Pending";

        // 之後管理員回覆用
        public string ApprovedTime { get; set; } = "";

        public string TableId { get; set; } = "";

        public string CadaverRowKey { get; set; } = "";

        public string AdminReply { get; set; } = "";
    }
}