using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace HBaseMVC.Services
{
    public class HBaseService
    {
        private readonly string baseUrl = "http://localhost:8080";
        private readonly string tableName = "pathology";

        public async Task<string> GetRowAsync(string rowKey)
        {
            using HttpClient client = new HttpClient();

            string url = $"{baseUrl}/{tableName}/{rowKey}";
            client.DefaultRequestHeaders.Add("Accept", "text/xml");

            HttpResponseMessage response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return "查詢失敗：" + response.StatusCode;

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<bool> PutCellAsync(string rowKey, string column, string value)
        {
            using HttpClient client = new HttpClient();

            string url = $"{baseUrl}/{tableName}/{rowKey}/{column}";

            HttpContent content = new StringContent(
                value,
                Encoding.UTF8,
                "application/octet-stream"
            );

            HttpResponseMessage response = await client.PutAsync(url, content);

            return response.IsSuccessStatusCode;
        }
    }
}