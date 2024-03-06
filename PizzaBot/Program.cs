using Microsoft.EntityFrameworkCore;
using PizzaBot.Components;
using PizzaBot.Models;
using PizzaBot.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

String connectionString = "server=" + Environment.GetEnvironmentVariable("DATABASE_URL") +
                            ";uid=" + Environment.GetEnvironmentVariable("DATABASE_USERNAME") +
                            ";pwd=" + Environment.GetEnvironmentVariable("DATABASE_PASSWD") +
                            ";database=" + Environment.GetEnvironmentVariable("DATABASE_NAME");
builder.Services.AddDbContext<PizzaContext>(options => options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)), ServiceLifetime.Singleton, ServiceLifetime.Singleton);
builder.Services.AddSingleton<JSONService>();
builder.Services.AddSingleton<PizzaBalancingService>();
builder.Services.AddSingleton<PizzaDBService>();
builder.Services.AddSingleton<ArchiveService>();

builder.Services.AddSingleton<GlobalStuffService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
