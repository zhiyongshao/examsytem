using System.Security.Cryptography;
using System.Text;

namespace ExamSystem;

/// <summary>
/// 演示用密码哈希（SHA256 + 固定 pepper）。生产环境建议替换为 BCrypt/scrypt。
/// </summary>
public static class PasswordHelper
{
    private const string Pepper = "ExamSystem@2026";

    public static string Hash(string password)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password + Pepper));
        return Convert.ToHexString(bytes);
    }

    public static bool Verify(string password, string hash)
        => Hash(password) == hash;

    /// <summary>生成随机登录密码（去掉易混淆字符 0/O/1/I/l），默认 6 位。</summary>
    public static string Random(int length = 6)
    {
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var rnd = System.Random.Shared;
        return new string(Enumerable.Repeat(alphabet, length).Select(a => a[rnd.Next(a.Length)]).ToArray());
    }

    /// <summary>生成 URL 安全的随机短链串（去掉易混淆字符 0/O/1/I/l，大小写同义保留），默认 8 位，用于考试独立入口 /e/xxx。</summary>
    public static string Slug(int length = 8)
    {
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var rnd = System.Random.Shared;
        return new string(Enumerable.Repeat(alphabet, length).Select(a => a[rnd.Next(a.Length)]).ToArray());
    }
}
