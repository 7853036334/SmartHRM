using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using SmartHRM.BusinessLogic;
using SmartHRM.DataAccess;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<IFaceService, FaceService>(client =>
{
    client.BaseAddress = new Uri("http://127.0.0.1:8000/");
    client.Timeout = Timeout.InfiniteTimeSpan;
});


builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));


builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowPythonAndLocal", policy =>
    {
        policy.WithOrigins("http://127.0.0.1:8000", "http://localhost:8000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
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


app.UseCors("AllowPythonAndLocal");


app.UseAuthentication();
app.UseAuthorization();


app.MapHub<SmartHRM.Presentation.Hubs.AttendanceHub>("/attendanceHub");
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();