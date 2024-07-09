using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenIddict.Abstractions.Managers
{
    public interface IOpenIddictNonceManager
    {
        void AddNonce(string nonce, DateTime expirationDate);

        void GenerateAndAddNonce();

        bool ValidateNonce(string nonce);
        
        void CleanExpiredNonces();

        string GetLatestNonce();
    }
}
