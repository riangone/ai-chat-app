using Microsoft.EntityFrameworkCore;
using PhotoManager.Models;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// 数据库配置
builder.Services.AddDbContext<PhotoDbContext>(options =>
    options.UseSqlite("Data Source=photos.db"));

var app = builder.Build();

// 静态文件与根目录配置
app.UseStaticFiles();
app.UseDefaultFiles();

// 确保上传目录存在
var uploadPath = Path.Combine(app.Environment.WebRootPath, "uploads");
if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);

// 初始化数据库
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PhotoDbContext>();
    db.Database.EnsureCreated();
}

// API 端点
app.MapGet("/api/photos", async (PhotoDbContext db) =>
{
    var photos = await db.Photos.OrderByDescending(p => p.UploadedAt).ToListAsync();
    return Results.Extensions.Html(RenderPhotoList(photos));
});

app.MapPost("/api/photos/upload", async (HttpRequest request, PhotoDbContext db) =>
{
    if (!request.HasFormContentType) return Results.BadRequest("Invalid form content");
    
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("photo");
    
    if (file == null || file.Length == 0) return Results.BadRequest("No file uploaded");

    var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
    var filePath = Path.Combine(uploadPath, fileName);

    using (var stream = new FileStream(filePath, FileMode.Create))
    {
        await file.CopyToAsync(stream);
    }

    var photo = new Photo { FileName = file.FileName, FilePath = $"/uploads/{fileName}" };
    db.Photos.Add(photo);
    await db.SaveChangesAsync();

    var photos = await db.Photos.OrderByDescending(p => p.UploadedAt).ToListAsync();
    return Results.Extensions.Html(RenderPhotoList(photos));
});

app.MapDelete("/api/photos/{id}", async (int id, PhotoDbContext db) =>
{
    var photo = await db.Photos.FindAsync(id);
    if (photo != null)
    {
        var fullPath = Path.Combine(app.Environment.WebRootPath, photo.FilePath.TrimStart('/'));
        if (File.Exists(fullPath)) File.Delete(fullPath);
        
        db.Photos.Remove(photo);
        await db.SaveChangesAsync();
    }
    
    var photos = await db.Photos.OrderByDescending(p => p.UploadedAt).ToListAsync();
    return Results.Extensions.Html(RenderPhotoList(photos));
});

app.Run();

// HTML 片段渲染器
string RenderPhotoList(List<Photo> photos)
{
    if (!photos.Any()) return "<p class='text-gray-500'>暂无照片，请上传。</p>";
    
    var html = "<div class='grid grid-cols-2 md:grid-cols-4 gap-4'>";
    foreach (var photo in photos)
    {
        html += $@"
            <div class='relative group border rounded-lg overflow-hidden shadow-sm'>
                <img src='{photo.FilePath}' class='w-full h-48 object-cover' alt='{photo.FileName}'>
                <div class='p-2 bg-white flex justify-between items-center'>
                    <span class='text-xs truncate max-w-[100px]'>{photo.FileName}</span>
                    <button hx-delete='/api/photos/{photo.Id}' hx-target='#photo-list' class='text-red-500 hover:text-red-700'>
                        删除
                    </button>
                </div>
            </div>";
    }
    html += "</div>";
    return html;
}

// 数据库上下文
class PhotoDbContext : DbContext
{
    public PhotoDbContext(DbContextOptions<PhotoDbContext> options) : base(options) { }
    public DbSet<Photo> Photos => Set<Photo>();
}

// 简单的 HTML 返回扩展
namespace Microsoft.AspNetCore.Http
{
    public static class ResultsExtensions
    {
        public static IResult Html(this IResultExtensions extensions, string html)
        {
            return new HtmlResult(html);
        }
    }

    public class HtmlResult : IResult
    {
        private readonly string _html;
        public HtmlResult(string html) => _html = html;
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "text/html";
            return httpContext.Response.WriteAsync(_html);
        }
    }
}
