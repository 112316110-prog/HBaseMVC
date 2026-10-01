namespace HBaseMVC.Models
{
    public class ClassSchedule
    {
        // HBase RowKeytest123
        public string RowKey { get; set; } = "";

        // 同一堂課的識別碼
        // 例如 CLASS_20260820_001
        public string ClassSessionId { get; set; } = "";

        // 上課日期
        // 例如 2026-08-20
        public string ClassDate { get; set; } = "";

        // surgical_table 的 RowKey
        public string TableId { get; set; } = "";

        // 大體老師在 cadaver table 的 RowKey
        public string CadaverRowKey { get; set; } = "";

        // =========================
        // 正式排課 / 報表資料
        // =========================

        // 組別
        public string GroupName { get; set; } = "";

        // 年齡
        public string Age { get; set; } = "";

        // 基本病史
        public string MedicalHistory { get; set; } = "";

        // 基本診斷
        public string Diagnosis { get; set; } = "";

        // 科別縮寫，例如 GS、CV、NS
        public string Department { get; set; } = "";

        // 參與學生
        public string Students { get; set; } = "";

        // 主要醫生
        public string MainDoctor { get; set; } = "";

        // 助手
        public string Assistant { get; set; } = "";

        // 護理人員
        public string Nurse { get; set; } = "";

        // 課程內容
        public string CourseContent { get; set; } = "";

        // 手術主題
        public string SurgeryTopic { get; set; } = "";

        // 手術方式 / 怎麼做
        public string Procedure { get; set; } = "";

        // 手術部位
        public string BodyPart { get; set; } = "";

        // 對應原始申請編號
        public string ApplicationRowKey { get; set; } = "";

        // 核准人
        public string ApprovedBy { get; set; } = "";

        // 核准時間
        public string ApprovedTime { get; set; } = "";
    }
}