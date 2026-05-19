using Dapper;
using Microsoft.EntityFrameworkCore;
using MozartWorkflows.Dtos;
using MozartWorkflows.Services.Interfaces;
using RulesEngine.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;                   // System.Text.Json for read & write (parity with API)
using System.Threading.Tasks;

namespace MozartWorkflows.Services
{
    public class RuleServiceImpl : IRuleService
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly IDbService _repository;

        public RuleServiceImpl(IDbConnectionFactory dbContext, IDbService repository)
        {
            _dbConnectionFactory = dbContext;
            _repository = repository;
        }

        // -------------------------------
        // Mapping (parity with API)
        // -------------------------------
        private RuleDtoForController MapToControllerDto(RuleDto dbDto)
        {
            return new RuleDtoForController
            {
                Id = dbDto.Id,
                WorkflowJson = !string.IsNullOrEmpty(dbDto.WorkflowJson)
                    ? JsonSerializer.Deserialize<Workflow>(dbDto.WorkflowJson)
                    : null,
                Active = dbDto.Active,
                CreatedAt = dbDto.CreatedAt,
                UpdatedAt = dbDto.UpdatedAt
            };
        }


        // ------------------------------------------
        // Paged fetch (sp_GetAllRules1) – API parity
        // ------------------------------------------
        public async Task<(IEnumerable<RuleDtoForController> Data, int TotalCount, int TotalPages)> GetAllRulesAsync(int page, int size)
        {
            
            var parameters = new DynamicParameters();
            parameters.Add("@PageNumber", page, DbType.Int32);
            parameters.Add("@PageSize", size, DbType.Int32);
            parameters.Add("@TotalCount", dbType: DbType.Int32, direction: ParameterDirection.Output);

            var rules = await _repository.ExecuteProcedureAsync<RuleDto>(
                "[dbo].[sp_GetAllRules1]",
                parameters
            );

            int totalCount = parameters.Get<int>("@TotalCount");
            int totalPages = (int)Math.Ceiling(totalCount / (double)size);

            var mappedRules = rules.Select(MapToControllerDto).ToList();
            
            return (mappedRules, totalCount, totalPages);
        }

        // -----------------------------------------
        // All for cache/engine – API parity
        // -----------------------------------------
        public async Task<IEnumerable<RuleDtoForController>> GetAllRulesAsync()
        {
            var result = await _repository.ExecuteProcedureAsync<RuleDto>("sp_GetAllRulesForCache", new { });
            return result.Select(MapToControllerDto).ToList();
        }

        // -----------------
        // Get by Id – API parity
        // -----------------
        public async Task<RuleDtoForController?> GetRuleByIdAsync(int id)
        {

            var result = await _repository.ExecuteProcedureAsync<RuleDto>(
                "sp_GetRuleById",
                new { Id = id }
            );

            var dbDto = result.FirstOrDefault();
            if (dbDto == null)
            {
                return null;
            }

            var mapped = MapToControllerDto(dbDto);
            
            return mapped;
        }

        // -----------
        // Add – API parity
        // -----------
        public async Task<RuleDtoForController> AddRuleAsync(RuleDtoForController rule)
        {

            using var connection = _dbConnectionFactory.CreateConnection();

            var newId = await connection.QuerySingleAsync<int>(
                "sp_AddRule",
                new
                {
                    WorkflowJson = rule.WorkflowJson != null
                        ? JsonSerializer.Serialize(rule.WorkflowJson) // System.Text.Json (same as API)
                        : null,
                    rule.Active
                },
                commandType: CommandType.StoredProcedure
            );

            rule.Id = newId;
            return rule;
        }

        // --------------
        // Update – API parity
        // --------------
        public async Task<RuleDtoForController> UpdateRuleAsync(RuleDtoForController rule)
        {

            using var connection = _dbConnectionFactory.CreateConnection();

            await connection.ExecuteAsync(
                "sp_UpdateRule",
                new
                {
                    rule.Id,
                    WorkflowJson = rule.WorkflowJson != null
                        ? JsonSerializer.Serialize(rule.WorkflowJson) // System.Text.Json (same as API)
                        : null,
                    rule.Active
                },
                commandType: CommandType.StoredProcedure
            );

            return rule;
        }

        // --------------
        // Delete – API parity
        // --------------
        public async Task DeleteRuleAsync(int id)
        {
            using var connection = _dbConnectionFactory.CreateConnection();
            await connection.ExecuteAsync(
                "sp_DeleteRule",
                new { Id = id },
                commandType: CommandType.StoredProcedure
            );

        }

        // --------------------
        // Get by Code – API parity
        // --------------------
        public async Task<RuleDtoForController?> GetRuleByCodeAsync(string ruleCode)
        {
            
            var result = await _repository.ExecuteProcedureAsync<RuleDto>(
                "sp_GetRuleByCode",
                new { RuleCode = ruleCode }
            );

            var dbDto = result.FirstOrDefault();
            if (dbDto == null)
            {
                return null;
            }

            var mapped = MapToControllerDto(dbDto);
            return mapped;
        }

        // ------------------------------------------------
        // Push into in-memory RulesEngine – API parity
        // ------------------------------------------------
        public async Task UpdateRulesInRuleEngine()
        {
            List<RuleDtoForController> rules = (await GetAllRulesAsync()).ToList();
            WorkflowExecutionService.updateRuleEngineWorkflows(rules);
        }

        // --------------------------
        // Names only – API parity
        // --------------------------
        public async Task<IEnumerable<string>> GetAllWorkflowNamesAsync()
        {

            using var connection = _dbConnectionFactory.CreateConnection();

            var result = await connection.QueryAsync<string>(
               "sp_GetAllWorkflowNames",
               commandType: CommandType.StoredProcedure
           );

            return result;
        }
    }
}
