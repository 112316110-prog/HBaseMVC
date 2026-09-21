namespace HBaseMVC.Models
{
    public class ClassSchedule
    {
        // HBase RowKey
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
    }
}