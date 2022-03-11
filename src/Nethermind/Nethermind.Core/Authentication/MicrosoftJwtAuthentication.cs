//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
// 

using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using Microsoft.IdentityModel.Tokens;
using Nethermind.JsonRpc;
using Nethermind.JsonRpc.Authentication;
using Nethermind.Logging;

namespace Nethermind.Core.Authentication;

public class MicrosoftJwtAuthentication : IRpcAuthentication
{
    private readonly SecurityKey _securityKey;
    private readonly IClock _clock;
    private const string JwtMessagePrefix = "Bearer ";
    private const int JwtTokenTtl = 5;
    private const int JwtSecretLength = 64;

    public MicrosoftJwtAuthentication(byte[] secret, IClock clock)
    {
        _securityKey = new SymmetricSecurityKey(secret);
        _clock = clock;
    }

    public static MicrosoftJwtAuthentication CreateFromHexSecret(string hexSecret, IClock clock)
    {
        byte[] decodedSecret = DecodeSecret(hexSecret);
        return new MicrosoftJwtAuthentication(decodedSecret, clock);
    }

    public static MicrosoftJwtAuthentication CreateFromFileOrGenerate(string filePath, IClock clock, ILogger logger)
    {
        FileInfo fileInfo = new(filePath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            // Generate secret;
            logger.Info("Generating jwt secret");
            byte[] secret = new byte[JwtSecretLength / 2];
            Random rnd = new();
            rnd.NextBytes(secret);
            Directory.CreateDirectory(fileInfo.DirectoryName!);
            StreamWriter writer = new(filePath);
            string hexSecret = EncodeSecret(secret);
            writer.Write(hexSecret);
            writer.Close();
            logger.Info($"Secret have been written to {fileInfo.FullName}");
            return new MicrosoftJwtAuthentication(secret, clock);
        }
        else
        {
            // Secret exists read from file
            logger.Info("Reading jwt secret from file");
            StreamReader stream = new(filePath);
            string hexSecret = stream.ReadToEnd();
            hexSecret = hexSecret.TrimStart().TrimEnd();
            if (!System.Text.RegularExpressions.Regex.IsMatch(hexSecret, @"^(0x)?[0-9a-fA-F]{64}$"))
            {
                throw new FormatException("Secret should be a 64 digit hexadecimal number");
            }
            return CreateFromHexSecret(hexSecret, clock);
        }
    }

    private static string EncodeSecret(byte[] secret)
    {
        return BitConverter.ToString(secret).Replace("-", "");
    }

    public bool Authenticate(string? token)
    {
        if (token == null) return false;
        if (!token.StartsWith(JwtMessagePrefix)) return false;
        token = token.Remove(0, JwtMessagePrefix.Length);
        TokenValidationParameters tokenValidationParameters = new TokenValidationParameters
        {
            IssuerSigningKey = _securityKey,
            RequireExpirationTime = false,
            ValidateLifetime = true,
            ValidateAudience = false,
            ValidateIssuer = false,
            LifetimeValidator = LifetimeValidator
        };

        try
        {
            JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
            SecurityToken securityToken;
            handler.ValidateToken(token, tokenValidationParameters, out securityToken);
            JwtSecurityToken jwtToken = handler.ReadJwtToken(token);
            long iat = ((DateTimeOffset)jwtToken.IssuedAt).ToUnixTimeSeconds();
            return Math.Abs(iat - _clock.GetCurrentTime()) <= JwtTokenTtl;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    private bool LifetimeValidator(
        DateTime? notBefore,
        DateTime? expires,
        SecurityToken securityToken,
        TokenValidationParameters validationParameters)
    {
        if (expires == null) return true;
        long exp = ((DateTimeOffset)expires).ToUnixTimeSeconds();
        return _clock.GetCurrentTime() < exp;
    }

    private static byte[] DecodeSecret(string hexSecret)
    {
        int start = hexSecret.StartsWith("0x") ? 2 : 0;
        return Enumerable.Range(start, hexSecret.Length - start)
            .Where(x => x % 2 == 0)
            .Select(x => Convert.ToByte(hexSecret.Substring(x, 2), 16))
            .ToArray();
    }
}
