using Availability.Infrastructure;
using Availability.Web.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
// Web conecta las capas al iniciar; los futuros controladores utilizarán Application.
builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("Availability"));
var querySource = builder.Configuration["EventQueries:Source"] ?? "Database";
if (querySource != "Demo" && querySource != "Database")
{
    throw new InvalidOperationException("EventQueries:Source debe ser Demo o Database.");
}
var useDemoData = querySource == "Demo";
if (useDemoData && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("El modo Demo solo está permitido en Development.");
}
builder.Services.AddEventQueries(useDemoData);
builder.Services.AddSingleton(new EventDisplayOptions(useDemoData));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
