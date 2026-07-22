using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BomAddIn.Core.Services;
using BomAddIn.Infrastructure.Models.Enums;
using BomAddIn.Dashboard;
using BomAddIn.UI.Import;
using BomAddIn.UI.TaskPane;
using Microsoft.Extensions.DependencyInjection;
using ExcelDna.Integration;
using ExcelDna.Integration.CustomUI;

namespace BomAddIn.Ribbon
{
    /// <summary>
    /// Ribbon 控制器 — 处理 Ribbon 按钮回调。
    /// Excel-DNA 通过 COM 接口调用此类。
    /// </summary>
    [ComVisible(true)]
    public class RibbonController : ExcelRibbon
    {
        private IServiceProvider Services => BomAddInStartup.ServiceProvider;

        /// <summary>TaskPane 显示委托（供 AutoOpen 注入）</summary>
        public static Action? ShowTaskPane { get; set; }

        public override string GetCustomUI(string ribbonId)
        {
            return @"
    <customUI xmlns='http://schemas.microsoft.com/office/2009/07/customui'>
      <ribbon>
        <tabs>
          <tab id='tabBomSuite' label='BOM Suite'>
            <group id='grpDashboard' label='仪表盘'>
              <button id='btnDashboard' label='打开仪表盘'
                      imageMso='ViewFormView' size='large'
                      onAction='OnOpenDashboard'/>
            </group>
            <group id='grpSync' label='数据'>
              <button id='btnSync' label='同步数据'
                      imageMso='SyncRefresh' size='large'
                      onAction='OnSyncData'/>
              <button id='btnImport' label='导入 BOM'
                      imageMso='FileOpen' size='large'
                      onAction='OnImportBom'/>
            </group>
            <group id='grpTools' label='工具'>
              <button id='btnTaskPane' label='任务窗格'
                      imageMso='CreateReportFromWizard' size='large'
                      onAction='OnOpenTaskPane'/>
            </group>
            <group id='grpInfo' label='信息'>
              <button id='btnAbout' label='关于'
                      imageMso='Info' size='large'
                      onAction='OnAbout'/>
            </group>
          </tab>
        </tabs>
      </ribbon>
    </customUI>";
        }

        public void OnOpenDashboard(IRibbonControl control) => DashboardBootstrapper.Show();

        public void OnOpenTaskPane(IRibbonControl control) => ShowTaskPane?.Invoke();

        private int _syncInProgress;

        /// <summary>同步 Ribbon 按钮 — 从当前用户上下文获取实际角色。</summary>
        /// <remarks>async void 是 Excel-DNA COM 回调的签名要求，内部包裹 try-catch 防止进程崩溃。</remarks>
        public async void OnSyncData(IRibbonControl control)
        {
            if (Interlocked.CompareExchange(ref _syncInProgress, 1, 0) != 0)
            {
                MessageBox.Show("同步已在进行中，请等待完成。", "BOM Suite",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                using var scope = Services.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<ISyncService>();
                var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
                var currentUser = authService.GetCurrentUser(0);
                var callerRole = currentUser?.Role ?? UserRole.Viewer;
                var result = await syncService.SyncAllAsync(callerRole);

                var message = result.Success
                    ? $"同步完成: {result.TotalRecords} 条记录"
                    : $"同步失败: {result.ErrorMessage}";

                MessageBox.Show(message, "BOM Suite — 同步结果",
                    MessageBoxButtons.OK,
                    result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步异常: {ex.Message}", "BOM Suite — 错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Interlocked.Exchange(ref _syncInProgress, 0);
            }
        }

        public async void OnImportBom(IRibbonControl control)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "导入 BOM 数据",
                Filter = "Excel 文件|*.xlsx;*.xls|CSV 文件|*.csv|所有文件|*.*",
                FilterIndex = 1
            };

            if (dialog.ShowDialog() != DialogResult.OK) return;

            try
            {
                // 1. 解析文件（后台执行，避免冻结 Excel UI）
                DataTable table = await Task.Run(() => BomAddIn.UI.Import.FileImportService.ParseFile(dialog.FileName));
                if (table.Rows.Count == 0)
                {
                    MessageBox.Show("文件中没有数据行。", "BOM Suite — 导入",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 2. 检测列映射
                var headers = new string[table.Columns.Count];
                for (int i = 0; i < table.Columns.Count; i++)
                    headers[i] = table.Columns[i].ColumnName;

                using var scope = Services.CreateScope();
                var importer = scope.ServiceProvider.GetRequiredService<IBomExcelImporter>();
                var mapping = importer.DetectColumnMapping(headers);

                // 3. 显示列映射并确认
                var hasItemCode = mapping.ContainsKey("ItemCode");
                var hasParentCode = mapping.ContainsKey("ParentItemCode");

                var mapSummary = new System.Text.StringBuilder();
                mapSummary.AppendLine("检测到以下列映射:");
                mapSummary.AppendLine();
                foreach (var kv in mapping)
                    mapSummary.AppendLine($"  {kv.Key} ← \"{kv.Value}\"");

                var unmapped = new List<string>();
                foreach (var h in headers)
                {
                    var trimmed = h.Trim();
                    if (!mapping.ContainsValue(trimmed))
                        unmapped.Add(trimmed);
                }
                if (unmapped.Count > 0)
                {
                    mapSummary.AppendLine();
                    mapSummary.AppendLine($"未识别的列 ({unmapped.Count}): {string.Join(", ", unmapped)}");
                }

                mapSummary.AppendLine();
                mapSummary.AppendLine($"共 {table.Rows.Count} 行数据。");

                if (hasItemCode)
                    mapSummary.AppendLine(hasParentCode
                        ? "→ 将导入 BOM 结构（有父项编码列）。"
                        : "→ 将导入物料数据（无父项编码列）。");
                else
                {
                    MessageBox.Show("未检测到物料编码列。\n请确保表头包含：物料编码、Item Code、Code 等。",
                        "BOM Suite — 列映射错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var confirmResult = MessageBox.Show(
                    mapSummary.ToString(),
                    "BOM Suite — 确认导入",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

                if (confirmResult != DialogResult.OK) return;

                // 4. 获取当前用户角色（遵循 OnSyncData 模式，从登录上下文获取实际角色）
                UserRole callerRole;
                using (var authScope = Services.CreateScope())
                {
                    var authService = authScope.ServiceProvider.GetRequiredService<IAuthService>();
                    var currentUser = authService.GetCurrentUser(0);
                    callerRole = currentUser?.Role ?? UserRole.Viewer;
                }

                // 5. 执行导入（后台执行，避免冻结 Excel UI）
                ImportResult result = await Task.Run(() =>
                {
                    using var importScope = Services.CreateScope();
                    var importImporter = importScope.ServiceProvider.GetRequiredService<IBomExcelImporter>();
                    return hasParentCode
                        ? importImporter.ImportBomStructures(table, 1, callerRole)
                        : importImporter.ImportMaterials(table, 1, callerRole);
                });

                // 6. 显示结果
                var resultMsg = new System.Text.StringBuilder();
                resultMsg.AppendLine(result.Success ? "导入完成!" : "导入完成（有错误）:");
                resultMsg.AppendLine();
                resultMsg.AppendLine($"  成功: {result.SuccessCount} 行");
                resultMsg.AppendLine($"  跳过: {result.Warnings.Count} 行");
                resultMsg.AppendLine($"  错误: {result.Errors.Count} 行");

                if (result.Warnings.Count > 0)
                {
                    resultMsg.AppendLine();
                    resultMsg.AppendLine("跳过详情:");
                    foreach (var w in result.Warnings.Take(5))
                        resultMsg.AppendLine($"  - {w}");
                    if (result.Warnings.Count > 5)
                        resultMsg.AppendLine($"  ... 及其他 {result.Warnings.Count - 5} 条");
                }

                if (result.Errors.Count > 0)
                {
                    resultMsg.AppendLine();
                    resultMsg.AppendLine("错误详情:");
                    foreach (var e in result.Errors.Take(5))
                        resultMsg.AppendLine($"  - {e}");
                    if (result.Errors.Count > 5)
                        resultMsg.AppendLine($"  ... 及其他 {result.Errors.Count - 5} 条");
                }

                MessageBox.Show(resultMsg.ToString(), "BOM Suite — 导入结果",
                    MessageBoxButtons.OK,
                    result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}", "BOM Suite — 错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void OnAbout(IRibbonControl control)
        {
            MessageBox.Show(
                "BOM Suite v1.1\n\n" +
                "企业级 BOM 管理与差异分析 Excel 插件\n" +
                "基于 Excel-DNA · SQLite + DuckDB\n\n" +
                "© 2026 BomAddIn Team",
                "关于 BOM Suite",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
