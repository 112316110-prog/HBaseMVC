namespace HBaseMVC.Models
{
    public sealed class AiChatRequest
    {
        public List<AiChatMessage> Messages { get; set; } = new();

        // true = AI 首頁建議主題
        // 只回答知識問題，不觸發病歷搜尋
        public bool IsSuggestedTopic { get; set; }
    }

    public sealed class AiChatMessage
    {
        public string Role { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
    }
}