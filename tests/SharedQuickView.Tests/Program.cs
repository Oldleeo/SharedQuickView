using IpPathQuickOpen;

var target = "192.168.1.100";
var failures = new List<string>();

void Check(string name, bool condition)
{
    if (!condition)
    {
        failures.Add(name);
    }
}

bool ThrowsArgumentException(Action action)
{
    try
    {
        action();
        return false;
    }
    catch (ArgumentException)
    {
        return true;
    }
}

var single = PathParser.ParseAndReplace(
    @"\\192.168.1.20\共享资料\设计部\产品图\春季新品",
    target);
Check("单路径数量", single.Count == 1);
Check("单路径完整替换", single.SingleOrDefault() ==
    @"\\192.168.1.100\共享资料\设计部\产品图\春季新品");

var deepFolder = PathParser.ParseAndReplace(
    @"\\\192.168.1.20\共享资料\设计部\产品图\系列 A\型号-16",
    "192.168.1.20");
Check("末级目录完整保留", deepFolder.SingleOrDefault() ==
    @"\\192.168.1.20\共享资料\设计部\产品图\系列 A\型号-16");

var bulkInput = """
\\\192.168.1.20\共享资料\设计部\产品图\春季新品
&#x20;\
\\192.168.1.20\共享资料\设计部\产品图\夏季 新品

\\192.168.1.20\共享资料\市场部\宣传素材
""";
var bulk = PathParser.ParseAndReplace(bulkInput, target);
Check("批量数量", bulk.Count == 3);
Check("带空格目录", bulk.Any(path => path.EndsWith(@"\夏季 新品")));

var quoted = PathParser.ParseAndReplace(
    "路径：‘//192.168.1.20/共享资料/设计部/产品图/测试款’",
    target);
Check("网页斜杠和引号", quoted.SingleOrDefault() == @"\\192.168.1.100\共享资料\设计部\产品图\测试款");

var webUrl = "https://192.168.1.20/共享资料/设计部?from=clipboard";
Check("网址不被当成UNC", PathParser.ParseAndReplace(webUrl, target).Count == 0);
Check("网址默认不识别", PathParser.ParseOpenTargets(webUrl, target, false, false).Count == 0);
var enabledWebUrl = PathParser.ParseOpenTargets(webUrl, target, true, false);
Check("网址开启后原样保留", enabledWebUrl.Count == 1
                                && enabledWebUrl[0].Kind == OpenTargetKind.WebUrl
                                && enabledWebUrl[0].Value == webUrl);
Check("非HTTP协议不打开", PathParser.ParseOpenTargets("ftp://192.168.1.20/share", target, true, false).Count == 0);

const string localPath = @"D:\设计资料\彩片\MO-16";
Check("本地路径默认不识别", PathParser.ParseOpenTargets(localPath, target, false, false).Count == 0);
var enabledLocalPath = PathParser.ParseOpenTargets($"本地路径：“{localPath}”", target, false, true);
Check("本地路径开启后原样保留", enabledLocalPath.Count == 1
                                    && enabledLocalPath[0].Kind == OpenTargetKind.LocalPath
                                    && enabledLocalPath[0].Value == localPath);

var mixedTargets = PathParser.ParseOpenTargets(
    $"{webUrl}\n\\\\192.168.1.20\\共享资料\\设计部\n{localPath}",
    target,
    true,
    true);
Check("混合格式批量识别", mixedTargets.Count == 3
                            && mixedTargets.Count(item => item.Kind == OpenTargetKind.SharedPath) == 1
                            && mixedTargets.Count(item => item.Kind == OpenTargetKind.WebUrl) == 1
                            && mixedTargets.Count(item => item.Kind == OpenTargetKind.LocalPath) == 1);

var duplicates = PathParser.ParseAndReplace(
    "\\\\192.168.1.20\\共享\\目录\n\\\\192.168.1.30\\共享\\目录",
    target);
Check("替换后去重", duplicates.Count == 1);
Check("IP校验", PathParser.IsValidTargetIp(target) && !PathParser.IsValidTargetIp("192.168.999.1"));

Check("拒绝非UNC路径", ThrowsArgumentException(() => PathLauncher.Open(@"C:\Documents")));
Check("版权主页", ExternalLinks.OldleeProfile == "https://x.com/oldleeoo");
Check("剪贴板自动打开默认关闭", !new AppSettings().AutoOpenClipboard);
var sameIpTransforms = PathParser.ParseTransforms(@"\\192.168.1.100\共享\目录", target);
Check("识别同目标IP", sameIpTransforms.Count == 1
                      && sameIpTransforms[0].SourceHost == target
                      && sameIpTransforms[0].ResultPath == @"\\192.168.1.100\共享\目录");
Check("同IP自动打开默认关闭", !new AppSettings().AutoOpenSameTargetIp);
Check("网址识别默认关闭", !new AppSettings().RecognizeWebUrls);
Check("本地路径识别默认关闭", !new AppSettings().RecognizeLocalPaths);

if (failures.Count > 0)
{
    Console.Error.WriteLine("FAILED: " + string.Join(", ", failures));
    Environment.Exit(1);
}

if (args.Length == 2 && args[0] == "--open-path")
{
    PathLauncher.Open(args[1]);
    Console.WriteLine("OPEN_SUCCESS: Windows Shell 已接受并打开目标 UNC 文件夹。");
}

Console.WriteLine("PASS: UNC、网址、本地路径、批量识别、去重和 IP 校验全部通过。");
