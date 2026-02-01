using System;
using System.Threading.Tasks;
using log4net;

namespace SAM.Extensions
{
    public static class TaskExtensions
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(TaskExtensions));

        public static async void SafeFireAndForget(this Task task, Action<Exception> onException = null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                onException?.Invoke(ex);
                
                // Fallback logging if no handler provided or just to be safe
                if (onException == null)
                {
                    log.Error("Unobserved exception in Fire-and-Forget task.", ex);
                }
            }
        }
    }
}
