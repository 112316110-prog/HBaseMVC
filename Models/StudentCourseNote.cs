namespace HBaseMVC.Models
{
    public class StudentCourseNote
    {
        // HBase RowKey
        public string RowKey { get; set; } = "";

        // 對應哪一堂課
        public string ClassSessionId { get; set; } = "";

        // 學生帳號 / 學號
        public string StudentId { get; set; } = "";

        // 大體老師 RowKey
        public string CadaverRowKey { get; set; } = "";

        // 上課日期
        public string ClassDate { get; set; } = "";

        // 學生自己的筆記
        public string Note { get; set; } = "";

        // 最後修改時間
        public string UpdatedAt { get; set; } = "";
    }
}