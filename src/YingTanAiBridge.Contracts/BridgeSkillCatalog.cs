using System;
using System.Collections.Generic;
using System.Linq;

namespace YingTanAiBridge.Contracts;

/// <summary>
/// Normalizes and selects declarative Skills. A Skill can influence planning but can never add
/// commands beyond the host adapter's explicit command allowlist.
/// </summary>
public static class BridgeSkillCatalog
{
    public static void Normalize(IEnumerable<BridgeAiSkillConfig> skills)
    {
        if (skills == null)
        {
            return;
        }

        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in skills)
        {
            if (skill == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(skill.Id) || !usedIds.Add(skill.Id))
            {
                skill.Id = Guid.NewGuid().ToString("N");
                usedIds.Add(skill.Id);
            }

            skill.Name = (skill.Name ?? string.Empty).Trim();
            skill.Description = (skill.Description ?? string.Empty).Trim();
            skill.Instructions = (skill.Instructions ?? string.Empty).Trim();
            skill.Version = string.IsNullOrWhiteSpace(skill.Version) ? "1.0.0" : skill.Version.Trim();
            skill.Category = string.IsNullOrWhiteSpace(skill.Category) ? "通用" : skill.Category.Trim();
            skill.Source = string.IsNullOrWhiteSpace(skill.Source) ? "本地" : skill.Source.Trim();
            skill.TrustLevel = string.IsNullOrWhiteSpace(skill.TrustLevel) ? "本地" : skill.TrustLevel.Trim();
            skill.HostKeys = NormalizeList(skill.HostKeys);
            skill.TriggerKeywords = NormalizeList(skill.TriggerKeywords);
            skill.RecommendedCommands = NormalizeList(skill.RecommendedCommands);
        }
    }

    public static List<BridgeAiSkillConfig> SelectForPrompt(
        IEnumerable<BridgeAiSkillConfig> skills,
        string hostKey,
        string userRequest,
        int maximumCount)
    {
        if (skills == null || maximumCount <= 0)
        {
            return new List<BridgeAiSkillConfig>();
        }

        var request = userRequest ?? string.Empty;
        var candidates = skills
            .Where(skill => skill != null && skill.Enabled && AppliesToHost(skill, hostKey))
            .Select(skill => new { Skill = skill, Score = RelevanceScore(skill, request) })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matches = candidates
            .Where(item => item.Score > 0)
            .Take(maximumCount)
            .Select(item => item.Skill)
            .ToList();
        if (matches.Count > 0)
        {
            return matches;
        }

        // An untagged general Skill is a single, conservative fallback for novel requests.
        return candidates
            .Where(item => item.Skill.TriggerKeywords.Count == 0 &&
                           string.Equals(item.Skill.Category, "通用", StringComparison.OrdinalIgnoreCase))
            .Take(1)
            .Select(item => item.Skill)
            .ToList();
    }

    private static bool AppliesToHost(BridgeAiSkillConfig skill, string hostKey)
    {
        return skill.HostKeys == null || skill.HostKeys.Count == 0 ||
            skill.HostKeys.Any(item => string.Equals(item, "all", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(item, hostKey, StringComparison.OrdinalIgnoreCase));
    }

    private static int RelevanceScore(BridgeAiSkillConfig skill, string request)
    {
        var score = 0;
        foreach (var keyword in skill.TriggerKeywords ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(keyword) &&
                request.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 10;
            }
        }

        if (!string.IsNullOrWhiteSpace(skill.Name) &&
            request.IndexOf(skill.Name, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 6;
        }

        return score;
    }

    private static List<string> NormalizeList(IEnumerable<string> values)
    {
        return (values ?? Enumerable.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
