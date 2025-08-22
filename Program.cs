using Microsoft.AspNetCore.SignalR;
using ServerCRM.FreeSwitchSer;
using ServerCRM.FreeSwitchService;
using ServerCRM.Models;
using ServerCRM.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDistributedMemoryCache();
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddTransient<ApiService>();
builder.Services.AddTransient<AuthService>();
builder.Services.AddSingleton<FreeSwitchManager>();
builder.Services.AddSingleton(typeof(ServerCRM.FreeSwitchService.CallEventsHub));
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
builder.Services.Configure<CRMSettings>(builder.Configuration.GetSection("CRMSettings"));

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



app.MapControllerRoute(
    name: "default",
    pattern: "{controller=FreeSwitch}/{action=MakeCall}/{id?}");

app.UseHttpsRedirection();
app.MapControllers();

app.UseRouting();

app.UseSession();
app.UseAuthorization();

app.UseDefaultFiles();
app.MapHub<CtiHub>("/ctihub");
app.MapHub<ServerCRM.FreeSwitchService.CallEventsHub>("/callEventsHub");


app.UseStaticFiles();
var hubContext = app.Services.GetRequiredService<IHubContext<CtiHub>>();
CTIConnectionManager.Configure(hubContext);

app.Run();
