var builder = WebApplication.CreateBuilder(args);

// 🔹 Add services
builder.Services.AddControllers();

// 🔹 Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        policy =>
        {
            policy.WithOrigins("http://localhost:4200")
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
});

// Add after builder.Build()
var app = builder.Build();
app.UseCors("AllowAngular");


// 🔹 Swagger middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Bingo API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

// 🔹 CORS middleware (ADD HERE)
app.UseCors("AllowAngular");

app.UseAuthorization();

app.MapControllers();

app.Run();
