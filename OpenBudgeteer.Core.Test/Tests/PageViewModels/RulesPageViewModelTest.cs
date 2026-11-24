using System;
using System.Linq;
using System.Threading.Tasks;
using OpenBudgeteer.Core.Common.Extensions;
using OpenBudgeteer.Core.Data.Entities.Models;
using OpenBudgeteer.Core.Test.Mocking;
using OpenBudgeteer.Core.Test.Mocking.Services;
using OpenBudgeteer.Core.ViewModels.EntityViewModels;
using OpenBudgeteer.Core.ViewModels.PageViewModels;
using Xunit;

namespace OpenBudgeteer.Core.Test.Tests.PageViewModels;

public class RulesPageViewModelTest
{
    private const string OriginalName = "Rule Set";
    private const string OriginalComparisonValue = "Original Value";

    private static TestData SetupTestData()
    {
        var mockDatabase = new MockDatabase();
        var serviceManager = new MockServiceManager(mockDatabase);

        var bucketGroup = new BucketGroup { Name = "Bucket Group", Position = 1 };
        serviceManager.BucketGroupService.Create(bucketGroup);

        var targetBucket = new Bucket
        {
            BucketGroupId = bucketGroup.Id,
            Name = "Target Bucket",
            ColorCode = "Red",
            ValidFrom = new DateOnly(2010, 1, 1),
            CurrentVersion = new BucketVersion { Version = 1, BucketType = 1 }
        };
        serviceManager.BucketService.Create(targetBucket);

        var newTargetBucket = new Bucket
        {
            BucketGroupId = bucketGroup.Id,
            Name = "New Target Bucket",
            ColorCode = "Blue",
            ValidFrom = new DateOnly(2010, 1, 1),
            CurrentVersion = new BucketVersion { Version = 1, BucketType = 1 }
        };
        serviceManager.BucketService.Create(newTargetBucket);

        var ruleSet = new BucketRuleSet
        {
            Id = Guid.NewGuid(),
            Priority = 1,
            Name = OriginalName,
            TargetBucketId = targetBucket.Id
        };
        var mappingRule = new MappingRule
        {
            Id = Guid.NewGuid(),
            BucketRuleSetId = ruleSet.Id,
            ComparisonField = (int)MappingRuleComparisonField.Payee,
            ComparisonType = (int)MappingRuleComparisonType.Contains,
            ComparisonValue = OriginalComparisonValue
        };
        mockDatabase.BucketRuleSets[ruleSet.Id] = ruleSet;
        mockDatabase.MappingRules[mappingRule.Id] = mappingRule;

        return new TestData(serviceManager, mockDatabase, ruleSet, targetBucket, newTargetBucket);
    }

    private static RuleSetViewModel CreateEditedClone(RulesPageViewModel viewModel,
        MockServiceManager serviceManager, Bucket newTargetBucket)
    {
        var clone = (RuleSetViewModel)Assert.Single(viewModel.RuleSets).Clone();
        clone.Priority = 42;
        clone.Name = "Edited Rule Set";
        clone.TargetBucket = BucketViewModel.CreateForListing(serviceManager, newTargetBucket);
        clone.MappingRules[0].ComparisonField = MappingRuleComparisonField.Memo;
        clone.MappingRules[0].ComparisonType = MappingRuleComparisonType.NotEqual;
        clone.MappingRules[0].ComparisonValue = "Edited Value";
        return clone;
    }

    [Fact]
    public async Task LoadDataAsync_CloneEdit_DoesNotChangeLoadedRuleSet()
    {
        var testData = SetupTestData();
        var viewModel = new RulesPageViewModel(testData.ServiceManager);
        var loadResult = await viewModel.LoadDataAsync();
        Assert.True(loadResult.IsSuccessful);

        var loaded = Assert.Single(viewModel.RuleSets);
        var clone = CreateEditedClone(viewModel, testData.ServiceManager, testData.NewTargetBucket);

        // The instance displayed by the Rules overview is not touched by the dialog edits
        Assert.Equal(testData.RuleSet.Id, loaded.BucketRuleSetId);
        Assert.Equal(1, loaded.Priority);
        Assert.Equal(OriginalName, loaded.Name);
        Assert.Equal(testData.TargetBucket.Id, loaded.TargetBucket.BucketId);
        Assert.Equal(MappingRuleComparisonField.Payee, loaded.MappingRules[0].ComparisonField);
        Assert.Equal(MappingRuleComparisonType.Contains, loaded.MappingRules[0].ComparisonType);
        Assert.Equal(OriginalComparisonValue, loaded.MappingRules[0].ComparisonValue);

        // The clone passed to the dialog carries the edited values
        Assert.Equal(testData.RuleSet.Id, clone.BucketRuleSetId);
        Assert.Equal(42, clone.Priority);
        Assert.Equal("Edited Rule Set", clone.Name);
        Assert.Equal(testData.NewTargetBucket.Id, clone.TargetBucket.BucketId);
        Assert.Equal(MappingRuleComparisonField.Memo, clone.MappingRules[0].ComparisonField);
        Assert.Equal(MappingRuleComparisonType.NotEqual, clone.MappingRules[0].ComparisonType);
        Assert.Equal("Edited Value", clone.MappingRules[0].ComparisonValue);
    }

    [Fact]
    public async Task SaveRuleSetItem_OnEditedClone_UpdatesExistingRuleSetAndReloads()
    {
        var testData = SetupTestData();
        var viewModel = new RulesPageViewModel(testData.ServiceManager);
        var loadResult = await viewModel.LoadDataAsync();
        Assert.True(loadResult.IsSuccessful);

        var clone = CreateEditedClone(viewModel, testData.ServiceManager, testData.NewTargetBucket);

        var saveResult = viewModel.SaveRuleSetItem(clone);
        Assert.True(saveResult.IsSuccessful);
        Assert.True(saveResult.ViewModelReloadRequired);

        // The existing rule set was updated in place, no second rule set was created
        Assert.Single(testData.MockDatabase.BucketRuleSets);
        Assert.True(testData.MockDatabase.BucketRuleSets.ContainsKey(testData.RuleSet.Id));
        var persistedRuleSet = testData.MockDatabase.BucketRuleSets[testData.RuleSet.Id];
        Assert.Equal(42, persistedRuleSet.Priority);
        Assert.Equal("Edited Rule Set", persistedRuleSet.Name);
        Assert.Equal(testData.NewTargetBucket.Id, persistedRuleSet.TargetBucketId);
        var persistedMappingRule = Assert.Single(persistedRuleSet.MappingRules!);
        Assert.Equal((int)MappingRuleComparisonField.Memo, persistedMappingRule.ComparisonField);
        Assert.Equal((int)MappingRuleComparisonType.NotEqual, persistedMappingRule.ComparisonType);
        Assert.Equal("Edited Value", persistedMappingRule.ComparisonValue);

        // Reloading the Rules overview from the data source shows the saved values
        var reloadResult = await viewModel.LoadDataAsync();
        Assert.True(reloadResult.IsSuccessful);
        var reloaded = Assert.Single(viewModel.RuleSets);
        Assert.Equal(testData.RuleSet.Id, reloaded.BucketRuleSetId);
        Assert.Equal(42, reloaded.Priority);
        Assert.Equal("Edited Rule Set", reloaded.Name);
        Assert.Equal(testData.NewTargetBucket.Id, reloaded.TargetBucket.BucketId);
    }

    [Fact]
    public async Task EditedCloneWithoutSave_DoesNotWriteToDataSource()
    {
        var testData = SetupTestData();
        var viewModel = new RulesPageViewModel(testData.ServiceManager);
        var loadResult = await viewModel.LoadDataAsync();
        Assert.True(loadResult.IsSuccessful);

        // Simulate the Cancel path: the dialog only ever edits the copy, nothing is saved
        CreateEditedClone(viewModel, testData.ServiceManager, testData.NewTargetBucket);

        Assert.Single(testData.MockDatabase.BucketRuleSets);
        Assert.Single(testData.MockDatabase.MappingRules);
        var storedRuleSet = testData.MockDatabase.BucketRuleSets[testData.RuleSet.Id];
        Assert.Equal(1, storedRuleSet.Priority);
        Assert.Equal(OriginalName, storedRuleSet.Name);
        Assert.Equal(testData.TargetBucket.Id, storedRuleSet.TargetBucketId);
        var storedMappingRule = testData.MockDatabase.MappingRules.Values.Single();
        Assert.Equal((int)MappingRuleComparisonField.Payee, storedMappingRule.ComparisonField);
        Assert.Equal((int)MappingRuleComparisonType.Contains, storedMappingRule.ComparisonType);
        Assert.Equal(OriginalComparisonValue, storedMappingRule.ComparisonValue);

        var reloadResult = await viewModel.LoadDataAsync();
        Assert.True(reloadResult.IsSuccessful);
        var reloaded = Assert.Single(viewModel.RuleSets);
        Assert.Equal(1, reloaded.Priority);
        Assert.Equal(OriginalName, reloaded.Name);
        Assert.Equal(testData.TargetBucket.Id, reloaded.TargetBucket.BucketId);
        Assert.Equal(MappingRuleComparisonField.Payee, reloaded.MappingRules[0].ComparisonField);
        Assert.Equal(MappingRuleComparisonType.Contains, reloaded.MappingRules[0].ComparisonType);
        Assert.Equal(OriginalComparisonValue, reloaded.MappingRules[0].ComparisonValue);
    }

    private sealed class TestData(
        MockServiceManager serviceManager,
        MockDatabase mockDatabase,
        BucketRuleSet ruleSet,
        Bucket targetBucket,
        Bucket newTargetBucket)
    {
        public MockServiceManager ServiceManager { get; } = serviceManager;
        public MockDatabase MockDatabase { get; } = mockDatabase;
        public BucketRuleSet RuleSet { get; } = ruleSet;
        public Bucket TargetBucket { get; } = targetBucket;
        public Bucket NewTargetBucket { get; } = newTargetBucket;
    }
}
