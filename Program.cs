using Microsoft.AspNetCore.SignalR;
using ServerCRM.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ApiService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(300);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(10); 
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30); 
});
builder.Services.AddSignalR();

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "Pages/LogIn.html" }
});
app.UseHttpsRedirection();
app.MapControllers();

app.UseRouting();

app.UseSession();
app.UseAuthorization();

app.UseDefaultFiles();
app.MapHub<CtiHub>("/ctihub");

app.UseStaticFiles();
var hubContext = app.Services.GetRequiredService<IHubContext<CtiHub>>();
CTIConnectionManager.Configure(hubContext);

app.Run();
