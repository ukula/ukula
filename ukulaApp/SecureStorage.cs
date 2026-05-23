using System;
using Windows.Security.Credentials;

namespace UkulaApp
{
    public static class SecureStorage
    {
        private const string RESOURCE = "UkulaApp";

        public static void Save(string key, string value)
        {
            try
            {
                var vault = new PasswordVault();

                try
                {
                    var old = vault.Retrieve(RESOURCE, key);
                    vault.Remove(old);
                }
                catch
                {
                }

                vault.Add(new PasswordCredential(
                    RESOURCE,
                    key,
                    value));
            }
            catch (Exception ex)
            {
                Logger.Log("SecureStorage Save failed", ex);
            }
        }

        public static string? Load(string key)
        {
            try
            {
                var vault = new PasswordVault();

                var credential = vault.Retrieve(
                    RESOURCE,
                    key);

                credential.RetrievePassword();

                return credential.Password;
            }
            catch
            {
                return null;
            }
        }

        public static void Delete(string key)
        {
            try
            {
                var vault = new PasswordVault();

                var credential = vault.Retrieve(
                    RESOURCE,
                    key);

                vault.Remove(credential);
            }
            catch
            {
            }
        }

        public static void DeleteAll()
        {
            try
            {
                var vault = new PasswordVault();

                var credentials =
                    vault.FindAllByResource(RESOURCE);

                foreach (var credential in credentials)
                {
                    vault.Remove(credential);
                }
            }
            catch
            {
            }
        }
    }
}