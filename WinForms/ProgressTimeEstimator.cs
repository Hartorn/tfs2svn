namespace Tfs2Svn.Winforms
{
    using System;

    public class ProgressTimeEstimator
    {
        private DateTime startTime;

        // private int _updatesRemaining;
        private int totalUpdates;

        // private DateTime _lastTimeMark;
        private int updateCount;

        public ProgressTimeEstimator(DateTime startTime, int totalUpdates)
        {
            this.startTime = startTime;
            this.totalUpdates = totalUpdates;
        }

        public string GetApproxTimeRemaining()
        {
            if (this.updateCount == 0)
            {
                return "Calculating...";
            }

            if (this.updateCount == this.totalUpdates)
            {
                return "Done.";
            }

            TimeSpan timespan = DateTime.Now - this.startTime;
            int updatesRemaining = this.totalUpdates - this.updateCount;

            double averageSeconds = timespan.TotalSeconds / this.updateCount;
            double secondsRemaining = averageSeconds * updatesRemaining;

            double minutesRemaining = secondsRemaining / 60.0;
            double hoursRemaining = minutesRemaining / 60.0;
            double daysRemaining = hoursRemaining / 24.0;

            daysRemaining = Math.Round(daysRemaining);
            hoursRemaining = Math.Round(hoursRemaining);
            minutesRemaining = Math.Round(minutesRemaining);

            if (daysRemaining > 0)
            {
                return string.Format("About {0} {1} remaining.", daysRemaining, this.PluralizeIfNeeded("day", daysRemaining));
            }

            if (hoursRemaining > 0)
            {
                return string.Format("About {0} {1} remaining.", hoursRemaining, this.PluralizeIfNeeded("hour", hoursRemaining));
            }

            if (minutesRemaining > 0)
            {
                return string.Format("About {0} {1} remaining.", minutesRemaining, this.PluralizeIfNeeded("minute", minutesRemaining));
            }

            return "Less than a minute remaining.";
        }

        public void Update()
        {
            ++this.updateCount;
        }

        private string PluralizeIfNeeded(string word, double count)
        {
            if (count == 0 || count > 1)
            {
                return word.TrimEnd("s".ToCharArray()) + "s";
            }

            return word;
        }
    }
}