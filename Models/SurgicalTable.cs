namespace HBaseMVC.Models
{
    public class SurgicalTable
    {
        // HBase RowKey
        public string RowKey { get; set; } = "";

        // 手術台編號，例如：1、2、3、A01
        public string TableNumber { get; set; } = "";

        // 顯示名稱，例如：手術台 1
        public string TableName { get; set; } = "";

        // 是否啟用
        public bool IsActive { get; set; } = true;
    }
}