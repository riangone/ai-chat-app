using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models;

public class Course
{
    public int Id { get; set; }
    public int UserId { get; set; }
    [Required] public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string IconEmoji { get; set; } = "📚";
    public string Color { get; set; } = "#58cc02";
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Lesson> Lessons { get; set; } = new();
}

public class Lesson
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    [Required] public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public string ExercisesJson { get; set; } = "[]";
    public int XpReward { get; set; } = 10;
    public Course? Course { get; set; }
}

public class UserLessonProgress
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int LessonId { get; set; }
    public int Score { get; set; }
    public int XpEarned { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
