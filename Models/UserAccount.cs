namespace HBaseMVC.Models
{
    public class UserAccount
    {
        public string Role { get; set; } = "";

        public string LoginId { get; set; } = "";

        public string Email { get; set; } = "";

        public string PasswordHash { get; set; } = "";
    }
}