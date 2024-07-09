using OpenIddict.Abstractions.Managers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenIddict.Core.NonceUtils;

public class OpenIddictNonceManager : IOpenIddictNonceManager
{
    private const string ValidChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";

    private ConcurrentQueue<(string, DateTime)> _nonces;

    public OpenIddictNonceManager()
    {
        _nonces = new ConcurrentQueue<(string, DateTime)>();
        this.GenerateAndAddNonce();
    }

    public string GetLatestNonce()
    {
        return _nonces.Last().Item1;
    }

    public void AddNonce(string nonce, DateTime expirationDate)
    {
        _nonces.Enqueue((nonce, expirationDate));
    }

    public void GenerateAndAddNonce()
    {
        string nonce = GenerateNonce();
        _nonces.Enqueue((nonce, DateTime.UtcNow.AddMinutes(10)));
    }

    public bool ValidateNonce(string nonce)
    {
        foreach (var (n, expirationDate) in _nonces)
        {
            if (n == nonce && expirationDate > DateTime.UtcNow)
            {
                return true;
            }
        }

        return false;
    }

    public void CleanExpiredNonces()
    {
        while (_nonces.TryPeek(out var nonce) && nonce.Item2 < DateTime.UtcNow)
        {
            _nonces.TryDequeue(out _);
        }
    }

    private string GenerateNonce()
    {
        Random random = new Random();

        StringBuilder nonceBuilder = new StringBuilder();

        for (int i = 0; i < 24; i++)
        {
            char randomChar = ValidChars[random.Next(ValidChars.Length)];
            nonceBuilder.Append(randomChar);
        }

        return nonceBuilder.ToString();
    }
}
