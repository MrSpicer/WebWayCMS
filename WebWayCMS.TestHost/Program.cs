using WebWayCMS;
using WebWayCMS.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWebWayCms(builder.Configuration);
builder.Host.UseCmsSerilog(builder.Configuration);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseStatusCodePagesWithReExecute("/Error/{0}");
}

app.EnsureCMS();   // applies migrations, seeds roles/admin + the default Home page,
                   // and configures the middleware pipeline + dynamic page routing
app.Run();
