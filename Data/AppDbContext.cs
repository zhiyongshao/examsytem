using Microsoft.EntityFrameworkCore;

namespace ExamSystem;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamRule> ExamRules => Set<ExamRule>();
    public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();
    public DbSet<ExamSession> ExamSessions => Set<ExamSession>();
    public DbSet<ExamAnswer> ExamAnswers => Set<ExamAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<Exam>().HasMany(e => e.Rules).WithOne().HasForeignKey(r => r.ExamId);
        modelBuilder.Entity<Exam>().HasMany(e => e.Questions).WithOne().HasForeignKey(q => q.ExamId);
        modelBuilder.Entity<ExamSession>().HasMany(s => s.Answers).WithOne().HasForeignKey(a => a.SessionId);
        modelBuilder.Entity<ExamQuestion>().HasOne(q => q.Question).WithMany().HasForeignKey(q => q.QuestionId);
    }
}
