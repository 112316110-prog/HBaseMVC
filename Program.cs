var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// 登入、驗證碼、角色權限會用到 Session
builder.Services.AddSession();

// 註冊 Ollama API 用的 HttpClient
builder.Services.AddHttpClient("Ollama", client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");
    client.Timeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// 一定要放在 UseRouting 後、UseAuthorization 前
app.UseSession();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Login}/{id?}");

app.Run();