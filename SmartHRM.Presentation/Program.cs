using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using SmartHRM.BusinessLogic;
using SmartHRM.DataAccess;

var builder = WebApplication.CreateBuilder(args);

// 1. Add MVC Controllers & SignalR
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();

// 2. Register FaceService as a Typed HttpClient
builder.Services.AddHttpClient<IFaceService, FaceService>(client =>
{
    client.BaseAddress = new Uri("http://127.0.0.1:8000/");
    client.Timeout = Timeout.InfiniteTimeSpan;
});

// 3. Register ApplicationDbContext with SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 4. Configure Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// 5. Configure CORS (Allows Python backend & frontend cross-talk)
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

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Enable CORS
app.UseCors("AllowPythonAndLocal");

// Authentication must precede Authorization
app.UseAuthentication();
app.UseAuthorization();

// 6. Map Hubs, API Controllers, and MVC Routes
app.MapHub<SmartHRM.Presentation.Hubs.AttendanceHub>("/attendanceHub");
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();