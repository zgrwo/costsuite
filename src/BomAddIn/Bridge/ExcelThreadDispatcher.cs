using System;
using System.Threading;
using System.Threading.Tasks;
using ExcelDna.Integration;

namespace BomAddIn.Bridge
{
    /// <summary>
    /// ExcelThreadDispatcher 实现。
    /// 在 AutoOpen 时捕获 Excel 主线程 ID，后续所有跨线程 COM 调用通过
    /// QueueAsMacro 封送回主线程执行。
    /// </summary>
    public class ExcelThreadDispatcher : IExcelThreadDispatcher
    {
        private static int _excelMainThreadId = -1;

        /// <summary>
        /// 在 AutoOpen 中调用一次，捕获当前线程作为 Excel 主线程。
        /// </summary>
        public static void Initialize()
        {
            _excelMainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public bool IsExcelMainThread =>
            Thread.CurrentThread.ManagedThreadId == _excelMainThreadId;

        public T RunOnExcelThread<T>(Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            // 已在主线程 → 直接执行，避免 QueueAsMacro 死锁
            if (IsExcelMainThread)
                return action();

            // QueueAsMacro 的 Action 重载返回 void，
            // 用 ManualResetEvent 同步等待结果
            T result = default!;
            Exception? error = null;
            using var waitHandle = new ManualResetEvent(false);
            ExcelAsyncUtil.QueueAsMacro(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    waitHandle.Set();
                }
            });
            if (!waitHandle.WaitOne(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Excel COM 调用超时（30 秒）。Excel 主线程可能正忙或已断开。");

            if (error != null)
                throw error;

            return result;
        }

        public Task<T> RunOnExcelThreadAsync<T>(Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (IsExcelMainThread)
                return Task.FromResult(action());

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            ExcelAsyncUtil.QueueAsMacro(() =>
            {
                try
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        bool set = tcs.TrySetResult(action());
                        if (!set)
                            Infrastructure.Logging.AppLogger.Debug("Result discarded: TCS already completed (timeout likely).", typeof(ExcelThreadDispatcher));
                    }
                }
                catch (Exception ex)
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        bool set = tcs.TrySetException(ex);
                        if (!set)
                            Infrastructure.Logging.AppLogger.Debug($"Exception discarded: TCS already completed (timeout likely). Error: {ex.Message}", typeof(ExcelThreadDispatcher));
                    }
                }
            });

            // 添加超时保护，避免后台线程无限等待
            var timeoutTask = Task.Delay(30000);
            return Task.WhenAny(tcs.Task, timeoutTask)
                .ContinueWith(t =>
                {
                    if (tcs.Task.IsCompleted)
                        return tcs.Task.Result;
                    tcs.TrySetCanceled();
                    throw new TimeoutException("Excel COM 异步调用超时（30 秒）。Excel 主线程可能正忙或已断开。");
                });
        }
    }
}
