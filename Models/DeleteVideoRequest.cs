namespace HBaseMVC.Models
{
    public class DeleteVideoRequest
    {
        public string RowKey { get; set; }
        public string VideoPath { get; set; }
    }
}
