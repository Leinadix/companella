namespace Companella.Extensions;

using System.Security.Cryptography;

public static class Md5ByteArrayExtensions
{
	public static string Md5(this byte[] data)
	{
#pragma warning disable CA5351 // MD5 used for non-security hashing
#pragma warning disable CA1850
		using var md5 = MD5.Create();
		var hash = md5.ComputeHash(data);
		return Convert.ToHexString(hash);
#pragma warning restore CA5351
#pragma warning restore CA1850
	}

	public static string Md5(this Stream data)
	{
#pragma warning disable CA5351 // MD5 used for non-security hashing
		using var md5 = MD5.Create();
		var hash = md5.ComputeHash(data);
		return Convert.ToHexString(hash);
#pragma warning restore CA5351
	}
}
