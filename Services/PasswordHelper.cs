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
}
