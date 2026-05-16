using ECommons.ExcelServices;
using ECommons.GameHelpers;
using Lumina.Excel.Sheets;
using static GluttonyCombo.CustomComboNS.Functions.Jobs;
using static GluttonyCombo.Window.Text;

namespace GluttonyCombo.Extensions
{
    internal static class JobExtensions
    {
        public static string Shorthand(this Job job) => JobNameLocalization.GetJobShortName(job);

        public static string Name(this Job job) => JobNameLocalization.GetJobName(job);

        public static string Name(this ClassJob job)
        {
            Job j = (Job)job.RowId;
            return j.Name();
        }

        public static bool MatchesPlayerJob(this JobRole role)
        {
            if (role == JobRole.All)
                return true;

            return role == GetRoleFromJob(Player.Job);
        }

    }
}
