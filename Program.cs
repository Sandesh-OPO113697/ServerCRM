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
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthorization();
app.UseStaticFiles();
app.UseDefaultFiles();
app.MapHub<CtiHub>("/ctihub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=LogIn}/{action=logInUser}/{id?}");
var hubContext = app.Services.GetRequiredService<IHubContext<CtiHub>>();
CTIConnectionManager.Configure(hubContext);

app.Run();
