using Dalamud.Logging;
using ECommons.DalamudServices;
using PunishLib.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace PunishLib.API
{
    internal static class API
    {
        internal static string APITestEndPoint = "https://puni.sh/api/test/auth?authKey=";
        public async static Task<bool> ValidateKey()
        {
            using HttpResponseMessage responseMessage = await new HttpClient().GetAsync(APITestEndPoint + PunishLibMain.SharedConfig.APIKey);
            Svc.Log.Debug($"{responseMessage.StatusCode} {responseMessage.ReasonPhrase}");
            if (responseMessage.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

    }
}
