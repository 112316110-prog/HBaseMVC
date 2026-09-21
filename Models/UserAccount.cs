namespace HBaseMVC.Models
{
    public class UserAccount
    {
        public string Role { get; set; } = "";      // Student / Teacher
        public string LoginId { get; set; } = "";   // 學號 / 教職員編號
        public string PasswordHash { get; set; } = "";
    }
}