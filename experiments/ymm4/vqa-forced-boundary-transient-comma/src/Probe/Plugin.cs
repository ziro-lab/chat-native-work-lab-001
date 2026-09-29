using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Voice;
using YmmTextDecoration = YukkuriMovieMaker.Commons.TextDecoration;

namespace Ymm4VqaForcedBoundaryProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VQA Forced Boundary Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    const string BaselineSerif = "これは、テスト音声です実験のために生成しました。";
    const string ForcedSerif = "これは、テスト音声です実<w0>験のために生成しました。";
    const string MultiSerif = "これは、テスト音<w0>声です実<w0>験のために生成しました。";
    const string AmbiguousSerif = "曖<w0>昧境界";
    const string PersistRemark = "CNWL_VQA_FORCED_BOUNDARY_PERSIST";

    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];
    static int nextFrame = 240;

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VQA_FORCED_BOUNDARY_OUTPUT");
        var url = Environment.GetEnvironmentVariable("CNWL_FAKE_VOICEVOX_URL");
        if (scheduled || string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(url))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => Start(url)),
            DispatcherPriority.ApplicationIdle);
    }

    static void Start(string url)
    {
        int ticks = 0;
        bool created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };

        timer.Tick += async (_, _) =>
        {
            try
            {
                ticks++;
                var main = Application.Current.Windows
                    .Cast<Window>()
                    .Select(x => x.DataContext)
                    .FirstOrDefault(x =>
                        x?.GetType().FullName
                        == "YukkuriMovieMaker.ViewModels.MainViewModel");

                if (main is null)
                    return;

                var active = GetActive(main);
                if (active is null)
                {
                    if (!created)
                    {
                        created = true;
                        PublicMethod(main, "CreateProject", Type.EmptyTypes)
                            .Invoke(main, null);
                    }

                    if (ticks > 160)
                        throw new TimeoutException("ActiveTimelineViewModel was not created.");
                    return;
                }

                timer.Stop();
                await RunAsync(main, url);
                Write("PASS_VQA_FORCED_BOUNDARY_TRANSIENT_COMMA", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VQA_FORCED_BOUNDARY_TRANSIENT_COMMA", ex.ToString());
            }
        };

        timer.Start();
    }

    static async Task RunAsync(object main, string url)
    {
        var marker = ParseMarkers(ForcedSerif);
        var expectedBoundary = marker.CleanText.IndexOf('験');
        Check("official_parser_marker_position",
            marker.Positions.SequenceEqual([expectedBoundary])
            && marker.CleanText == BaselineSerif);

        var (speaker, parameter, registration) = CreateFakeSpeaker(url);
        try
        {
            var reload = await RunSaveReloadAsync(main, speaker, parameter);

            var timeline = FindTimeline(GetActive(main)
                ?? throw new InvalidOperationException("Active timeline missing after reload."))
                ?? throw new InvalidOperationException("Timeline missing after reload.");

            var character = new Character
            {
                Name = "CNWL VQA",
                Voice = new VoiceDescription(speaker),
                VoiceParameter = parameter
            };

            var baseline = await RunBaselineAsync(
                timeline, character, speaker, parameter);

            var forced = await RunVoiceCaseAsync(
                timeline, character, speaker, parameter,
                ForcedSerif, "FORCED");

            Check("transient_input_injects_comma",
                forced.Analysis.TransientText
                    == "これは、テスト音声です実、験のために生成しました。"
                && CountChar(forced.Analysis.TransientText, '、') == 2);

            Check("analysis_adds_phrase_boundary",
                baseline.PhraseCount == 2
                && forced.Analysis.PhraseCount == baseline.PhraseCount + 1
                && forced.Analysis.InjectedTargetIndices.Length == 1);

            Check("source_comma_independent",
                forced.Analysis.SourceCommaIndices.SequenceEqual([0])
                && forced.Analysis.InjectedTargetIndices.SequenceEqual([1]));

            Check("injected_pause_unique",
                forced.Analysis.InjectedCandidateCounts.SequenceEqual([1])
                && forced.Analysis.InjectedTargetIndices.Distinct().Count() == 1);

            Check("only_injected_pause_zero",
                ZeroPauseIndices(forced.Analysis.PauseAfter)
                    .SequenceEqual(forced.Analysis.InjectedTargetIndices));

            Check("source_comma_pause_preserved",
                forced.Analysis.PauseBefore[0] is > 0.0
                && forced.Analysis.PauseAfter[0] == forced.Analysis.PauseBefore[0]);

            Check("final_public_synthesis_succeeds",
                forced.Analysis.FinalSynthesis
                && !string.IsNullOrWhiteSpace(forced.Voice.FilePath)
                && File.Exists(forced.Voice.FilePath)
                && new FileInfo(forced.Voice.FilePath).Length > 44
                && forced.Voice.Pronounce is not null);

            Check("durable_serif_preserved",
                forced.Voice.Serif == ForcedSerif);

            Check("durable_hatsuon_has_no_injected_marker",
                forced.Voice.Hatsuon == marker.CleanText
                && !forced.Voice.Hatsuon.Contains("<w0>", StringComparison.Ordinal)
                && CountChar(forced.Voice.Hatsuon, '、') == 1
                && CountChar(forced.Analysis.TransientText, '、') == 2);

            var multi = await RunVoiceCaseAsync(
                timeline, character, speaker, parameter,
                MultiSerif, "MULTI");

            Check("multiple_markers_distinct_zero_pauses",
                multi.Analysis.Applied
                && multi.Analysis.InjectedTargetIndices.Length == 2
                && multi.Analysis.InjectedTargetIndices.Distinct().Count() == 2
                && multi.Analysis.InjectedCandidateCounts.All(x => x == 1)
                && ZeroPauseIndices(multi.Analysis.PauseAfter)
                    .SequenceEqual(multi.Analysis.InjectedTargetIndices)
                && multi.Analysis.SourceCommaIndices.SequenceEqual([0])
                && multi.Analysis.PauseAfter[0] is > 0.0);

            var ambiguous = await AnalyzeSourceAsync(
                AmbiguousSerif,
                speaker,
                parameter,
                helperPosition: null,
                helperText: null,
                finalPath: null,
                caseName: "ambiguous");

            Check("ambiguous_mapping_fails_closed",
                !ambiguous.Applied
                && ambiguous.InjectedCandidateCounts.SequenceEqual([2])
                && ambiguous.PauseBefore.SequenceEqual(ambiguous.PauseAfter)
                && ZeroPauseIndices(ambiguous.PauseAfter).Length == 0);

            var helperClean = ParseMarkers(ForcedSerif).CleanText;
            var helperPosition = helperClean.IndexOf('声');
            var helper = await RunVoiceCaseAsync(
                timeline, character, speaker, parameter,
                ForcedSerif, "HELPER",
                helperPosition,
                "ヌ");

            Check("helper_coexists_with_boundary_identity",
                helper.Analysis.Applied
                && helper.Analysis.InjectedCandidateCounts.SequenceEqual([1])
                && helper.Analysis.InjectedTargetIndices.SequenceEqual([1])
                && helper.Analysis.HelperMoraCount == 1
                && helper.Analysis.HelperZeroCount == 1
                && helper.Analysis.PauseAfter[1] == 0.0
                && helper.Analysis.PauseAfter[0] is > 0.0);

            Check("save_reload_rederives_boundary",
                reload.MarkerRestored
                && reload.HatsuonRestored
                && reload.DifferentObject
                && reload.Reanalysis.Applied
                && reload.Reanalysis.InjectedCandidateCounts.SequenceEqual([1])
                && reload.Reanalysis.InjectedTargetIndices.SequenceEqual([1])
                && reload.Reanalysis.PauseAfter[1] == 0.0);

            File.WriteAllText(
                Path.Combine(output, "forced-boundary-observation.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    acceptanceFixture = new
                    {
                        baseline = BaselineSerif,
                        forced = ForcedSerif,
                        forced.Analysis.TransientText
                    },
                    baseline,
                    forced = ToSerializable(forced.Analysis),
                    multi = ToSerializable(multi.Analysis),
                    ambiguous = ToSerializable(ambiguous),
                    helper = ToSerializable(helper.Analysis),
                    reload = new
                    {
                        reload.ProjectA,
                        reload.ProjectB,
                        reload.MarkerRestored,
                        reload.HatsuonRestored,
                        reload.DifferentObject,
                        reanalysis = ToSerializable(reload.Reanalysis)
                    }
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            registration.Restore();
        }
    }

    static object ToSerializable(AnalysisResult x) => new
    {
        x.Applied,
        x.Failure,
        x.SourceSerif,
        x.CleanText,
        x.TransientText,
        x.MarkerPositions,
        x.PhraseCount,
        x.InjectedTargetIndices,
        x.SourceCommaIndices,
        x.InjectedCandidateCounts,
        x.PauseBefore,
        x.PauseAfter,
        x.FullReadingMatchesMoras,
        x.FinalSynthesis,
        x.HelperText,
        x.HelperMoraCount,
        x.HelperZeroCount,
        x.Accents
    };

    static async Task<BaselineObservation> RunBaselineAsync(
        Timeline timeline,
        Character character,
        IVoiceSpeaker speaker,
        IVoiceParameter parameter)
    {
        var voice = CreateVoice(
            character, parameter, BaselineSerif, BaselineSerif, "BASELINE");
        Require(timeline.TryAddItems([voice], nextFrame, 5), "baseline timeline add");
        nextFrame += 180;
        await voice.CreateVoiceFileAsync();

        var wav = Path.Combine(output, "baseline-public-analysis.wav");
        var pronounce = await speaker.CreateVoiceAsync(
            BaselineSerif, pronounce: null, parameter, wav)
            ?? throw new InvalidOperationException("Baseline analysis returned null Pronounce.");
        var phrases = GetPhraseStates(pronounce);
        return new BaselineObservation(
            phrases.Count,
            phrases.Select(x => x.Accent).ToArray(),
            phrases.Select(x => PauseLength(x.PauseMora)).ToArray());
    }

    static async Task<VoiceCaseObservation> RunVoiceCaseAsync(
        Timeline timeline,
        Character character,
        IVoiceSpeaker speaker,
        IVoiceParameter parameter,
        string serif,
        string remark,
        int? helperPosition = null,
        string? helperText = null)
    {
        var clean = ParseMarkers(serif).CleanText;
        var voice = CreateVoice(character, parameter, serif, clean, remark);
        Require(timeline.TryAddItems([voice], nextFrame, 5), remark + " timeline add");
        nextFrame += 180;

        await voice.CreateVoiceFileAsync();
        var path = voice.FilePath;
        Require(!string.IsNullOrWhiteSpace(path) && File.Exists(path), remark + " baseline file");

        var analysis = await AnalyzeSourceAsync(
            serif, speaker, parameter,
            helperPosition, helperText,
            path, remark.ToLowerInvariant());

        if (analysis.Applied && analysis.FinalPronounce is not null)
        {
            voice.ClearVoiceCache();
            voice.Pronounce = analysis.FinalPronounce;
        }

        return new VoiceCaseObservation(voice, analysis);
    }

    static async Task<AnalysisResult> AnalyzeSourceAsync(
        string sourceSerif,
        IVoiceSpeaker speaker,
        IVoiceParameter parameter,
        int? helperPosition,
        string? helperText,
        string? finalPath,
        string caseName)
    {
        var parsed = ParseMarkers(sourceSerif);
        var plan = BuildTransient(
            parsed.CleanText,
            parsed.Positions,
            helperPosition,
            helperText);

        var analysisPath = Path.Combine(
            output, $"{caseName}-analysis-{Guid.NewGuid():N}.wav");

        var pronounce = await speaker.CreateVoiceAsync(
            plan.Text,
            pronounce: null,
            parameter,
            analysisPath)
            ?? throw new InvalidOperationException(caseName + ": analysis returned null Pronounce.");

        var phrases = GetPhraseStates(pronounce);
        var before = phrases.Select(x => PauseLength(x.PauseMora)).ToArray();
        var accents = phrases.Select(x => x.Accent).ToArray();

        var fullYomi = await speaker.ConvertKanjiToYomiAsync(plan.Text, parameter);
        var flattenedMoras = string.Concat(
            phrases.SelectMany(x => x.Moras).Select(MoraText));
        var fullMatch =
            NormalizeReading(fullYomi ?? "") == NormalizeReading(flattenedMoras);

        var injectedMaps = new List<PrefixMap>();
        foreach (var prefix in plan.InjectedPrefixes)
            injectedMaps.Add(
                await ResolvePrefixAsync(prefix, speaker, parameter, phrases));

        var sourceMaps = new List<PrefixMap>();
        foreach (var prefix in plan.SourceCommaPrefixes)
            sourceMaps.Add(
                await ResolvePrefixAsync(prefix, speaker, parameter, phrases));

        var injectedCandidateCounts = injectedMaps
            .Select(x => x.Candidates.Length)
            .ToArray();

        bool mappingSafe =
            fullMatch
            && injectedMaps.All(x => x.Candidates.Length == 1)
            && injectedMaps.Select(x => x.Candidates[0]).Distinct().Count()
                == injectedMaps.Count
            && injectedMaps.All(
                x => phrases[x.Candidates[0]].PauseMora is not null);

        var sourceIndices = sourceMaps
            .Where(x => x.Candidates.Length == 1)
            .Select(x => x.Candidates[0])
            .ToArray();

        int[] injectedIndices = mappingSafe
            ? injectedMaps.Select(x => x.Candidates[0]).ToArray()
            : [];

        if (mappingSafe && injectedIndices.Intersect(sourceIndices).Any())
            mappingSafe = false;

        var helperMoras = string.IsNullOrEmpty(helperText)
            ? []
            : phrases.SelectMany(x => x.Moras)
                .Where(x => MoraText(x) == helperText)
                .ToArray();

        if (!string.IsNullOrEmpty(helperText) && helperMoras.Length != 1)
            mappingSafe = false;

        if (!mappingSafe)
        {
            var unchanged = phrases.Select(
                x => PauseLength(x.PauseMora)).ToArray();
            return new AnalysisResult(
                Applied: false,
                Failure: "ambiguous-or-invalid-mapping",
                SourceSerif: sourceSerif,
                CleanText: parsed.CleanText,
                TransientText: plan.Text,
                MarkerPositions: parsed.Positions,
                PhraseCount: phrases.Count,
                InjectedTargetIndices: [],
                SourceCommaIndices: sourceIndices,
                InjectedCandidateCounts: injectedCandidateCounts,
                PauseBefore: before,
                PauseAfter: unchanged,
                FullReadingMatchesMoras: fullMatch,
                FinalSynthesis: false,
                HelperText: helperText,
                HelperMoraCount: helperMoras.Length,
                HelperZeroCount: helperMoras.Count(
                    x => VowelLength(x) == 0.0),
                Accents: accents,
                FinalPronounce: null);
        }

        foreach (var index in injectedIndices)
            SetVowelLength(phrases[index].PauseMora!, 0.0);

        foreach (var helper in helperMoras)
            SetVowelLength(helper, 0.0);

        IVoicePronounce finalPronounce = pronounce;
        bool finalSynthesis = false;
        if (!string.IsNullOrWhiteSpace(finalPath))
        {
            finalPronounce = await speaker.CreateVoiceAsync(
                plan.Text,
                pronounce,
                parameter,
                finalPath)
                ?? throw new InvalidOperationException(
                    caseName + ": corrected synthesis returned null.");
            finalSynthesis =
                File.Exists(finalPath) && new FileInfo(finalPath).Length > 44;
        }

        var finalPhrases = GetPhraseStates(finalPronounce);
        var after = finalPhrases.Select(
            x => PauseLength(x.PauseMora)).ToArray();
        var finalHelperCount = string.IsNullOrEmpty(helperText)
            ? 0
            : finalPhrases.SelectMany(x => x.Moras)
                .Count(x => MoraText(x) == helperText);
        var finalHelperZero = string.IsNullOrEmpty(helperText)
            ? 0
            : finalPhrases.SelectMany(x => x.Moras)
                .Count(
                    x => MoraText(x) == helperText
                         && VowelLength(x) == 0.0);

        return new AnalysisResult(
            Applied: true,
            Failure: null,
            SourceSerif: sourceSerif,
            CleanText: parsed.CleanText,
            TransientText: plan.Text,
            MarkerPositions: parsed.Positions,
            PhraseCount: finalPhrases.Count,
            InjectedTargetIndices: injectedIndices,
            SourceCommaIndices: sourceIndices,
            InjectedCandidateCounts: injectedCandidateCounts,
            PauseBefore: before,
            PauseAfter: after,
            FullReadingMatchesMoras: fullMatch,
            FinalSynthesis: finalSynthesis,
            HelperText: helperText,
            HelperMoraCount: finalHelperCount,
            HelperZeroCount: finalHelperZero,
            Accents: finalPhrases.Select(x => x.Accent).ToArray(),
            FinalPronounce: finalPronounce);
    }

    static async Task<PrefixMap> ResolvePrefixAsync(
        string prefix,
        IVoiceSpeaker speaker,
        IVoiceParameter parameter,
        IReadOnlyList<PhraseState> phrases)
    {
        var yomi = await speaker.ConvertKanjiToYomiAsync(prefix, parameter)
            ?? "";
        var normalized = NormalizeReading(yomi);
        var cumulative = new StringBuilder();
        var candidates = new List<int>();

        foreach (var phrase in phrases)
        {
            foreach (var mora in phrase.Moras)
                cumulative.Append(MoraText(mora));

            if (NormalizeReading(cumulative.ToString()) == normalized)
                candidates.Add(phrase.Index);
        }

        return new PrefixMap(
            prefix, yomi, normalized, candidates.ToArray());
    }

    static MarkerParse ParseMarkers(string text)
    {
        var parsed = ControlTagParser.Parse(
            text,
            ImmutableList<YmmTextDecoration>.Empty,
            32.0,
            "Yu Gothic UI",
            false,
            false);

        static bool IsBoundary(TimingTag tag)
            => tag.Type.ToString() == "Wait"
               && Math.Abs(Convert.ToDouble(
                   tag.Value, CultureInfo.InvariantCulture)) < 0.000001
               && tag.Operator.ToString() == "Set";

        return new MarkerParse(
            parsed.Item1,
            parsed.Item3
                .Where(IsBoundary)
                .Select(x => x.Position)
                .ToArray());
    }

    static TransientPlan BuildTransient(
        string cleanText,
        IReadOnlyCollection<int> markers,
        int? helperPosition,
        string? helperText)
    {
        var markerSet = markers.ToHashSet();
        var sb = new StringBuilder();
        var injectedPrefixes = new List<string>();
        var sourceCommaPrefixes = new List<string>();

        for (int i = 0; i <= cleanText.Length; i++)
        {
            if (helperPosition == i && !string.IsNullOrEmpty(helperText))
                sb.Append(helperText);

            if (markerSet.Contains(i))
            {
                injectedPrefixes.Add(sb.ToString());
                sb.Append('、');
            }

            if (i == cleanText.Length)
                break;

            if (cleanText[i] == '、')
                sourceCommaPrefixes.Add(sb.ToString());

            sb.Append(cleanText[i]);
        }

        return new TransientPlan(
            sb.ToString(),
            injectedPrefixes.ToArray(),
            sourceCommaPrefixes.ToArray());
    }

    static List<PhraseState> GetPhraseStates(
        IVoicePronounce pronounce)
    {
        var query = pronounce.GetType().GetProperty(
            "AudioQuery",
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(pronounce)
            ?? throw new InvalidOperationException(
                "AudioQuery unavailable.");

        var phrases = query.GetType().GetProperty(
            "AccentPhrases",
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(query) as IEnumerable
            ?? throw new InvalidOperationException(
                "AccentPhrases unavailable.");

        var result = new List<PhraseState>();
        int index = 0;
        foreach (var phrase in phrases.Cast<object>())
        {
            var moras = phrase.GetType().GetProperty(
                "Moras",
                BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(phrase) as IEnumerable
                ?? throw new InvalidOperationException(
                    "Moras unavailable.");

            var pause = phrase.GetType().GetProperty(
                "PauseMora",
                BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(phrase);

            var accentValue = phrase.GetType().GetProperty(
                "Accent",
                BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(phrase);

            result.Add(new PhraseState(
                index++,
                moras.Cast<object>().ToList(),
                pause,
                accentValue is null
                    ? -1
                    : Convert.ToInt32(
                        accentValue, CultureInfo.InvariantCulture)));
        }

        return result;
    }

    static string MoraText(object mora) =>
        mora.GetType().GetProperty(
            "Text",
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(mora)?.ToString() ?? "";

    static double VowelLength(object mora)
    {
        var value = mora.GetType().GetProperty(
            "VowelLength",
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(mora)
            ?? throw new InvalidOperationException(
                "VowelLength unavailable.");
        return Convert.ToDouble(
            value, CultureInfo.InvariantCulture);
    }

    static double? PauseLength(object? mora) =>
        mora is null ? null : VowelLength(mora);

    static void SetVowelLength(object mora, double value)
    {
        var property = mora.GetType().GetProperty(
            "VowelLength",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                mora.GetType().FullName, "VowelLength");

        if (property.SetMethod?.IsPublic != true)
            throw new InvalidOperationException(
                "VowelLength is not publicly writable.");

        property.SetValue(mora, value);
    }

    static string NormalizeReading(string value)
    {
        var sb = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            var text = rune.ToString();
            if (text is "'" or "’" or "、" or "。" or "，" or ","
                or " " or "　" or "/" or "？" or "?" or "！" or "!")
                continue;
            sb.Append(text);
        }
        return sb.ToString();
    }

    static int[] ZeroPauseIndices(double?[] pauses) =>
        pauses.Select((value, index) => (value, index))
            .Where(x => x.value == 0.0)
            .Select(x => x.index)
            .ToArray();

    static int CountChar(string text, char value) =>
        text.Count(x => x == value);

    static VoiceItem CreateVoice(
        Character character,
        IVoiceParameter parameter,
        string serif,
        string hatsuon,
        string remark)
    {
        var voice = new VoiceItem
        {
            Serif = serif,
            Hatsuon = hatsuon,
            CharacterName = character.Name,
            VoiceParameter = parameter,
            Remark = "CNWL_" + remark
        };

        voice.Character = character;
        voice.VoiceParameter = parameter;
        voice.Serif = serif;
        voice.Hatsuon = hatsuon;
        voice.CharacterName = character.Name;
        return voice;
    }

    static async Task<ReloadObservation> RunSaveReloadAsync(
        object main,
        IVoiceSpeaker speaker,
        IVoiceParameter parameter)
    {
        var active = GetActive(main)
            ?? throw new InvalidOperationException(
                "Active timeline missing.");
        var timeline = FindTimeline(active)
            ?? throw new InvalidOperationException(
                "Timeline missing.");

        var simpleCharacter = new Character
        {
            Name = "CNWL VQA Persist"
        };
        var clean = ParseMarkers(ForcedSerif).CleanText;
        var voice = new VoiceItem(simpleCharacter)
        {
            Serif = ForcedSerif,
            Hatsuon = clean,
            Remark = PersistRemark,
            Frame = 60,
            Layer = 3,
            Length = 120
        };

        Require(
            timeline.TryAddItems([voice], voice.Frame, voice.Layer),
            "persist voice add");

        var saveProject =
            PublicMethod(main, "SaveProject", typeof(string));
        var openProject =
            PublicMethod(main, "OpenProject", typeof(string));

        var pathA = Path.Combine(
            output, "forced-boundary-a.ymmp");
        var pathB = Path.Combine(
            output, "forced-boundary-b.ymmp");

        saveProject.Invoke(main, [pathA]);
        await WaitUntil(
            "project A save",
            () => File.Exists(pathA)
                  && new FileInfo(pathA).Length > 0);

        voice.Serif = "壊したB";
        voice.Hatsuon = "こわしたびー";
        saveProject.Invoke(main, [pathB]);
        await WaitUntil(
            "project B save",
            () => File.Exists(pathB)
                  && new FileInfo(pathB).Length > 0);

        openProject.Invoke(main, [pathA]);
        await WaitUntil(
            "project A reopen",
            () => SamePath(GetProjectFilePath(main), pathA)
               && FindVoice(main, PersistRemark) is not null,
            12000);

        var reloaded = FindVoice(main, PersistRemark)
            ?? throw new InvalidOperationException(
                "Reloaded persist VoiceItem missing.");

        var reanalysisPath = Path.Combine(
            output, "reload-rederived-corrected.wav");
        var reanalysis = await AnalyzeSourceAsync(
            reloaded.Serif,
            speaker,
            parameter,
            helperPosition: null,
            helperText: null,
            finalPath: reanalysisPath,
            caseName: "reload");

        return new ReloadObservation(
            pathA,
            pathB,
            reloaded.Serif == ForcedSerif,
            reloaded.Hatsuon == clean,
            !ReferenceEquals(voice, reloaded),
            reanalysis);
    }

    static VoiceItem? FindVoice(
        object main,
        string remark)
    {
        var active = GetActive(main);
        var timeline =
            active is null ? null : FindTimeline(active);
        return timeline?.Items.OfType<VoiceItem>()
            .FirstOrDefault(x => x.Remark == remark);
    }

    static object? GetActive(object main) =>
        main.GetType().GetProperty(
            "ActiveTimelineViewModel",
            BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic)
            ?.GetValue(main);

    static MethodInfo PublicMethod(
        object target,
        string name,
        params Type[] types) =>
        target.GetType().GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types,
            modifiers: null)
        ?? throw new MissingMethodException(
            target.GetType().FullName, name);

    static string? GetProjectFilePath(object main)
    {
        var property = main.GetType().GetProperty(
            "ProjectFilePath",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                main.GetType().FullName, "ProjectFilePath");
        var reactive = property.GetValue(main);
        return reactive?.GetType().GetProperty(
            "Value",
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(reactive) as string;
    }

    static bool SamePath(
        string? actual,
        string expected) =>
        !string.IsNullOrWhiteSpace(actual)
        && string.Equals(
            Path.GetFullPath(actual),
            Path.GetFullPath(expected),
            StringComparison.OrdinalIgnoreCase);

    static async Task WaitUntil(
        string name,
        Func<bool> condition,
        int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
                return;
            await Task.Delay(100);
        }
        throw new TimeoutException(name);
    }

    static (
        IVoiceSpeaker Speaker,
        IVoiceParameter Parameter,
        SettingsRegistration Registration)
        CreateFakeSpeaker(string url)
    {
        var engine =
            new VOICEVOXEngine(new VOICEVOXEngineContext())
            {
                Name = "CNWL VQA VOICEVOX",
                URL = url,
                Path = "",
                Timeout = 10_000
            };

        const string fakeSpeakerUuid =
            "11111111-1111-1111-1111-111111111111";
        engine.SpeakerInfos.Add(
            new VOICEVOXSpeakerInfo(fakeSpeakerUuid, ""));

        var speakerJson = JObject.Parse("""
        {
          "name": "CNWL VQA",
          "speaker_uuid": "11111111-1111-1111-1111-111111111111",
          "styles": [
            { "name": "Normal", "id": 1, "type": "talk" }
          ],
          "version": "0.0.0",
          "supported_features": {
            "permitted_synthesis_morphing": "SELF_ONLY"
          }
        }
        """);

        engine.SpeakersJsonCache =
            new JArray(speakerJson)
                .ToString(Newtonsoft.Json.Formatting.None);
        Require(
            engine.Characters.Any(
                x => x.SpeakerUuid == fakeSpeakerUuid),
            "fake engine character");

        var vvCharacter = new VOICEVOXCharacter(
            speakerJson,
            Array.Empty<VOICEVOXSpeakerInfo>(),
            false);

        var speakerType =
            typeof(VOICEVOXEngine).Assembly.GetType(
                "YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker")
            ?? throw new TypeLoadException(
                "VOICEVOXVoiceSpeaker");

        var speakerObject = Activator.CreateInstance(
            speakerType,
            engine,
            vvCharacter)
            ?? throw new InvalidOperationException(
                "VOICEVOXVoiceSpeaker construction failed.");

        var speaker =
            speakerObject as IVoiceSpeaker
            ?? throw new InvalidOperationException(
                "Built-in VOICEVOX speaker unavailable.");

        var registration =
            RegisterEngineInYmmSettings(
                engine,
                speaker.ID);

        var parameter =
            speaker.CreateVoiceParameter();
        parameter.GetType().GetProperty(
            "StyleID",
            BindingFlags.Instance | BindingFlags.Public)
            ?.SetValue(parameter, 1);

        return (speaker, parameter, registration);
    }

    sealed record SettingsRegistration(
        object? ResolvedEngine,
        Action Restore);

    static SettingsRegistration RegisterEngineInYmmSettings(
        VOICEVOXEngine engine,
        string speakerId)
    {
        var settingsType =
            typeof(VOICEVOXEngine).Assembly.GetType(
                "YukkuriMovieMaker.Settings.VOICEVOXSettings")
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings type not found.");

        var defaultProperty =
            settingsType.GetProperty(
                "Default",
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.FlattenHierarchy)
            ?? settingsType.BaseType?.GetProperty(
                "Default",
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.FlattenHierarchy)
            ?? throw new MissingMemberException(
                settingsType.FullName,
                "Default");

        var settings = defaultProperty.GetValue(null)
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings.Default null.");

        var enginesProperty =
            settingsType.GetProperty(
                "Engines",
                BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                settingsType.FullName,
                "Engines");

        var original = enginesProperty.GetValue(settings)
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings.Engines null.");

        var add = original.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(x =>
                x.Name == "Add"
                && x.GetParameters().Length == 1
                && x.GetParameters()[0].ParameterType
                    .IsAssignableFrom(
                        typeof(VOICEVOXEngine)))
            ?? throw new MissingMethodException(
                original.GetType().FullName,
                "Add");

        var augmented = add.Invoke(original, [engine])
            ?? throw new InvalidOperationException(
                "Engines.Add null.");
        enginesProperty.SetValue(settings, augmented);

        var findEngine =
            settingsType.GetMethod(
                "FindEngine",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: [typeof(string)],
                modifiers: null)
            ?? throw new MissingMethodException(
                settingsType.FullName,
                "FindEngine");

        var resolved =
            findEngine.Invoke(settings, [speakerId]);
        Require(
            resolved is not null
            && ReferenceEquals(resolved, engine),
            "fake engine registration");

        return new SettingsRegistration(
            resolved,
            () =>
            {
                try
                {
                    enginesProperty.SetValue(
                        settings, original);
                }
                catch
                {
                }
            });
    }

    static Timeline? FindTimeline(object active)
    {
        for (var type = active.GetType();
             type is not null;
             type = type.BaseType)
        {
            foreach (var field in type.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic))
            {
                if (typeof(Timeline)
                        .IsAssignableFrom(field.FieldType)
                    && field.GetValue(active)
                        is Timeline timeline)
                {
                    return timeline;
                }
            }

            foreach (var property in type.GetProperties(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0
                    || !typeof(Timeline).IsAssignableFrom(
                        property.PropertyType))
                {
                    continue;
                }

                try
                {
                    if (property.GetValue(active)
                        is Timeline timeline)
                    {
                        return timeline;
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    static void Require(
        bool value,
        string name)
    {
        if (!value)
            throw new InvalidOperationException(
                "Required setup failed: " + name);
    }

    static void Check(
        string id,
        bool passed)
    {
        requirements.Add(new { id, passed });
        if (!passed)
            throw new InvalidOperationException(
                "Requirement failed: " + id);
    }

    static void Write(
        string status,
        string? error)
    {
        File.WriteAllText(
            Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema =
                    "cnwl.vqa-forced-boundary-transient-comma.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead =
                    Environment.GetEnvironmentVariable(
                        "GITHUB_SHA"),
                requirements,
                error
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    sealed record MarkerParse(
        string CleanText,
        int[] Positions);

    sealed record TransientPlan(
        string Text,
        string[] InjectedPrefixes,
        string[] SourceCommaPrefixes);

    sealed record PhraseState(
        int Index,
        List<object> Moras,
        object? PauseMora,
        int Accent);

    sealed record PrefixMap(
        string Prefix,
        string Yomi,
        string Normalized,
        int[] Candidates);

    sealed record BaselineObservation(
        int PhraseCount,
        int[] Accents,
        double?[] PauseLengths);

    sealed record VoiceCaseObservation(
        VoiceItem Voice,
        AnalysisResult Analysis);

    sealed record ReloadObservation(
        string ProjectA,
        string ProjectB,
        bool MarkerRestored,
        bool HatsuonRestored,
        bool DifferentObject,
        AnalysisResult Reanalysis);

    sealed record AnalysisResult(
        bool Applied,
        string? Failure,
        string SourceSerif,
        string CleanText,
        string TransientText,
        int[] MarkerPositions,
        int PhraseCount,
        int[] InjectedTargetIndices,
        int[] SourceCommaIndices,
        int[] InjectedCandidateCounts,
        double?[] PauseBefore,
        double?[] PauseAfter,
        bool FullReadingMatchesMoras,
        bool FinalSynthesis,
        string? HelperText,
        int HelperMoraCount,
        int HelperZeroCount,
        int[] Accents,
        IVoicePronounce? FinalPronounce);
}
