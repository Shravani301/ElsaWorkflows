using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MozartWorkflows.Models;
using MozartWorkflows.Services.Interfaces;
using Newtonsoft.Json;
using RulesEngine.Models;

namespace MozartWorkflows.Services
{
    public class RuleDataService(IRulesRepository repo) : IRuleDataService
    {
        private readonly IRulesRepository _repo = repo;

        // Engines per workflow
        private static readonly ConcurrentDictionary<string, (RulesEngine.RulesEngine Engine, string Hash, int RuleSetJsonId)> _engineCache = new();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _engineLocks = new();

        // Case data per CaseId with TTL
        private static readonly TimeSpan CaseTtl = TimeSpan.FromMinutes(10);
        private static readonly ConcurrentDictionary<string, (IReadOnlyDictionary<string, object?> Flat, string RawHash, DateTime CachedAt)> _caseCache = new();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _caseLocks = new();

        // Old per-RuleSet defaults (keep for compatibility if still used somewhere)
        private static readonly ConcurrentDictionary<int, (IReadOnlyDictionary<string, object?> Values, string Hash, DateTime CachedAt)> _defaultsCache = new();
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _defaultsLocks = new();

        // NEW: Global defaults cached once (keyed by constant)
        private const string GlobalDefaultsKey = "defaultValues";
        private static readonly ConcurrentDictionary<string, (IReadOnlyDictionary<string, object?> Values, string Hash, DateTime CachedAt)> _globalDefaultsCache = new();
        private static readonly SemaphoreSlim _globalDefaultsLock = new(1, 1);

        public async Task<(RulesEngine.RulesEngine Engine, int RuleSetJsonId)> GetRulesEngineAsync(int applicationId, string workflowName)
        {
            if (_engineCache.TryGetValue(workflowName, out var hit))
                return (hit.Engine, hit.RuleSetJsonId);

            var gate = _engineLocks.GetOrAdd(workflowName, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_engineCache.TryGetValue(workflowName, out hit))
                    return (hit.Engine, hit.RuleSetJsonId);

                var rulesJsonResult = await _repo.GetRulesJsonAsync(applicationId, workflowName).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"No rules found for appId={applicationId}, workflow={workflowName}.");

                // ✅ Deconstruct the tuple directly (no .Value)
                (string rulesJson, int ruleSetJsonId) = rulesJsonResult;

                var hash = ComputeHash(rulesJson);

                if (_engineCache.TryGetValue(workflowName, out var existing) && existing.Hash == hash)
                    return (existing.Engine, existing.RuleSetJsonId);

                var workflows = JsonConvert.DeserializeObject<List<Workflow>>(rulesJson)
                                ?? throw new InvalidOperationException("Invalid rules JSON.");
                var engine = new RulesEngine.RulesEngine(workflows.ToArray());

                var packed = (engine, hash, ruleSetJsonId);
                _engineCache[workflowName] = packed;
                return (packed.engine, packed.ruleSetJsonId);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<IReadOnlyDictionary<string, object?>> GetFlattenedCaseDataAsync(string caseId)
        {
            if (_caseCache.TryGetValue(caseId, out var cached) &&
                (DateTime.UtcNow - cached.CachedAt) <= CaseTtl &&
                cached.Flat.Count > 0)
                return cached.Flat;

            var gate = _caseLocks.GetOrAdd(caseId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_caseCache.TryGetValue(caseId, out cached) &&
                    (DateTime.UtcNow - cached.CachedAt) <= CaseTtl &&
                    cached.Flat.Count > 0)
                    return cached.Flat;

                var raw = await _repo.GetCaseDataJsonAsync(caseId).ConfigureAwait(false)
                          ?? throw new InvalidOperationException($"No CaseData for {caseId}");
                var hash = ComputeHash(raw);

                if (cached.Flat is not null && cached.RawHash == hash)
                {
                    var refreshed = (cached.Flat, hash, DateTime.UtcNow);
                    _caseCache[caseId] = refreshed;
                    return refreshed.Flat;
                }

                var flat = FormDataHelper.Flatten(raw);
                var snapshot = new Dictionary<string, object?>(flat);

                var packed = ((IReadOnlyDictionary<string, object?>)snapshot, hash, DateTime.UtcNow);
                _caseCache[caseId] = packed;
                return packed.Item1;
            }
            finally
            {
                gate.Release();
            }
        }

        // OLD per-ruleset method (optional to keep)
        public async Task<IReadOnlyDictionary<string, object?>> GetDefaultParamsAsync(int ruleSetJsonId)
        {
            if (_defaultsCache.TryGetValue(ruleSetJsonId, out var cached))
                return cached.Values;

            var gate = _defaultsLocks.GetOrAdd(ruleSetJsonId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_defaultsCache.TryGetValue(ruleSetJsonId, out cached))
                    return cached.Values;

                var json = await _repo.GetDefaultValuesAsync(ruleSetJsonId).ConfigureAwait(false)
                           ?? throw new InvalidOperationException($"No default values for RuleSetJsonId={ruleSetJsonId}");
                var hash = ComputeHash(json);

                if (_defaultsCache.TryGetValue(ruleSetJsonId, out var ex) && ex.Hash == hash)
                    return ex.Values;

                var dict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json)
                           ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                var snapshot = new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase);
                var packed = ((IReadOnlyDictionary<string, object?>)snapshot, hash, DateTime.UtcNow);
                _defaultsCache[ruleSetJsonId] = packed;
                return packed.Item1;
            }
            finally
            {
                gate.Release();
            }
        }

        // NEW: global defaults (load once, cache under "defaultValues")
        public async Task<IReadOnlyDictionary<string, object?>> GetDefaultParamsAsync()
        {
            if (_globalDefaultsCache.TryGetValue(GlobalDefaultsKey, out var cached))
                return cached.Values;

            await _globalDefaultsLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_globalDefaultsCache.TryGetValue(GlobalDefaultsKey, out cached))
                    return cached.Values;

                var json = await _repo.GetAllDefaultValuesAsync().ConfigureAwait(false)
                           ?? throw new InvalidOperationException("No global default values found.");
                var hash = ComputeHash(json);

                if (_globalDefaultsCache.TryGetValue(GlobalDefaultsKey, out var ex) && ex.Hash == hash)
                    return ex.Values;

                var dict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json)
                           ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                var snapshot = new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase);
                var packed = ((IReadOnlyDictionary<string, object?>)snapshot, hash, DateTime.UtcNow);
                _globalDefaultsCache[GlobalDefaultsKey] = packed;
                return packed.Item1;
            }
            finally
            {
                _globalDefaultsLock.Release();
            }
        }

        public void InvalidateCase(string caseId)
        {
            _caseCache.TryRemove(caseId, out _);
            if (_caseLocks.TryRemove(caseId, out var g)) g.Dispose();
        }

        public void InvalidateWorkflow(string workflowName)
        {
            _engineCache.TryRemove(workflowName, out _);
            if (_engineLocks.TryRemove(workflowName, out var g)) g.Dispose();
        }

        public void InvalidateRuleSet(int ruleSetJsonId)
        {
            _defaultsCache.TryRemove(ruleSetJsonId, out _);
            if (_defaultsLocks.TryRemove(ruleSetJsonId, out var g)) g.Dispose();
        }

        public void InvalidateGlobalDefaults()
        {
            _globalDefaultsCache.TryRemove(GlobalDefaultsKey, out _);
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }
}