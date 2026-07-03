using System;
using System.Collections.Generic;
using Core.Dependency;
using Core.Localization;
using Game.Core.Content;
using Game.Core.Content.Meta;
using Game.Platforms;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Data;
using Micalobia.Shapez2.FolderIcons.Features;
using Micalobia.Shapez2.FolderIcons.HUD;
using Micalobia.Shapez2.FolderIcons.Services;
using ShapezShifter.SharpDetour;
using ILogger = Core.Logging.ILogger;

namespace Micalobia.Shapez2.FolderIcons;

[UsedImplicitly]
public class FolderIcons : DisposableTracker, IMod
{
    private ILogger Logger { get; }
    private DependencyContainer RootContainer { get; } = new();
    public DependencyContainer CurrentSessionContainer { get; private set; }
    private readonly List<Action<DependencyContainer>> _serviceBindings = [];
    private readonly List<Func<DependencyContainer, IInitHUD>> _hudInitializers = [];

    public FolderIcons(ILogger logger)
    {
        Logger = logger;
        try
        {
            RootContainer.Bind<FolderIcons>().To(this);
            RootContainer.Bind<ILogger>().To(logger);

            RegisterServices();
            RegisterGameSessionInitHooks();
        }
        catch (Exception ex)
        {
            base.Dispose();
            RootContainer.Dispose();
            Logger.Warning?.Log($"Mod failed to load. Installed handlers were removed. Reason: {ex}");
            throw;
        }
    }

    #region Session Init Functions

    private void RegisterGameSessionInitHooks()
    {
        Track(DetourHelper.CreatePrefixHook(
            (GameSessionOrchestrator orchestrator,
                    IGameStartOptions gameStartOptions,
                    GlobalsData globals,
                    IGameData gameData,
                    IPlatform platform,
                    IContent content,
                    IContentMetadataProviderCollection contentMetadata) =>
                orchestrator.Prepare(gameStartOptions, globals, gameData, platform, content, contentMetadata),
            GameSessionOrchestrator_Prepare_Prefix
        ));
        Track(DetourHelper.CreatePostfixHook(
            (GameSessionOrchestrator orchestrator,
                    Keybindings keybindings,
                    IGameData gameData,
                    ILocalizationManager localizationManager,
                    ILocalizationResolver localizationResolver) =>
                orchestrator.Init_4_1_EssentialDependencies(keybindings, gameData, localizationManager, localizationResolver),
            GameSessionOrchestrator_Init_4_1_EssentialDependencies_Postfix
        ));
        Track(DetourHelper.CreatePostfixHook(
            (GameSessionOrchestrator orchestrator) => orchestrator.Init_8_HUD(),
            GameSessionOrchestrator_Init_8_HUD_Postfix
        ));
    }

    private (IGameStartOptions, GlobalsData, IGameData, IPlatform, IContent, IContentMetadataProviderCollection) GameSessionOrchestrator_Prepare_Prefix(
        GameSessionOrchestrator orchestrator,
        IGameStartOptions gameStartOptions,
        GlobalsData globals,
        IGameData gameData,
        IPlatform platform,
        IContent content,
        IContentMetadataProviderCollection contentMetadata)
    {
        DisposeCurrentSessionContainer();
        CurrentSessionContainer = RootContainer.CreateChildContainer();
        BindSessionDependencies(
            orchestrator,
            gameStartOptions,
            globals,
            gameData,
            platform,
            content,
            contentMetadata
        );

        foreach (var bind in _serviceBindings)
            bind(CurrentSessionContainer);

        return (gameStartOptions, globals, gameData, platform, content, contentMetadata);
    }

    private void GameSessionOrchestrator_Init_4_1_EssentialDependencies_Postfix(GameSessionOrchestrator orchestrator,
        Keybindings keybindings,
        IGameData gameData,
        ILocalizationManager localizationManager,
        ILocalizationResolver localizationResolver)
    {
        try
        {
            BindToSession<IBlueprintLibrary>(orchestrator.DependencyContainer);
        }
        catch (Exception ex)
        {
            DisposeCurrentSessionContainer();
            Logger.Warning?.Log($"Folder Icons session initialization failed. Initialized handlers were removed. Reason: {ex}");
            throw;
        }

        Logger.Info?.Log("Initialized Folder Icons session hooks.");
    }

    private void GameSessionOrchestrator_Init_8_HUD_Postfix(GameSessionOrchestrator orchestrator)
    {
        try
        {
            foreach (var resolve in _hudInitializers)
                resolve(CurrentSessionContainer).InitHUD(orchestrator);
        }
        catch (Exception ex)
        {
            DisposeCurrentSessionContainer();
            Logger.Warning?.Log($"Folder Icons HUD initialization failed. Initialized handlers were removed. Reason: {ex}");
            throw;
        }

        Logger.Info?.Log("Initialized Folder Icons HUD dependencies.");
    }

    private void RegisterServices()
    {
        RegisterRoot<AssetHandler>();
        Register<FolderMetadataSerializer>();
        Register<FolderMetadataHandler, FolderMetadataHandler.HookAdapter>();
        Register<IconHandler, IconHandler.HookAdapter>();
        Register<HUDBlueprintLibraryNavEntryHandler, HUDBlueprintLibraryNavEntryHandler.HookAdapter>();
        Register<SortingHandler, SortingHandler.HookAdapter>();
        Register<EditDialogHandler, EditDialogHandler.HookAdapter>();
        Register<HUDBlueprintLibraryHandler, HUDBlueprintLibraryHandler.HookAdapter>();
    }

    private void Register<T>() where T : class, ISessionService
    {
        _serviceBindings.Add(container => container.Bind<T>().To<T>());

        if (typeof(IInitHUD).IsAssignableFrom(typeof(T)))
            _hudInitializers.Add(container => (IInitHUD)container.Resolve<T>());
    }

    private void RegisterRoot<T>() where T : class, ISessionService => RootContainer.Bind<T>().To<T>();

    private void Register<THandler, TAdapter>()
        where THandler : class, ISessionService
        where TAdapter : HookAdapterBase
    {
        Register<THandler>();

        var adapter = RootContainer.Create<TAdapter>();
        RootContainer.Bind<TAdapter>().To(adapter);
        adapter.InstallHooks();
        Track(adapter);
    }

    public T ResolveSession<T>() where T : class => CurrentSessionContainer.Resolve<T>();

    public override void Dispose()
    {
        base.Dispose();
        RootContainer.Dispose();
        CurrentSessionContainer = null;
    }

    private void BindSessionDependencies(
        GameSessionOrchestrator orchestrator,
        IGameStartOptions gameStartOptions,
        GlobalsData globals,
        IGameData gameData,
        IPlatform platform,
        IContent content,
        IContentMetadataProviderCollection contentMetadata)
    {
        CurrentSessionContainer.Bind<GameSessionOrchestrator>().To(orchestrator);
        CurrentSessionContainer.Bind<IGameStartOptions>().To(gameStartOptions);
        CurrentSessionContainer.Bind<GlobalsData>().To(globals);
        CurrentSessionContainer.Bind<IGameData>().To(gameData);
        CurrentSessionContainer.Bind<IPlatform>().To(platform);
        CurrentSessionContainer.Bind<IContent>().To(content);
        CurrentSessionContainer.Bind<IContentMetadataProviderCollection>().To(contentMetadata);
    }

    private void BindToSession<T>(DependencyContainer source) where T : class => CurrentSessionContainer.Bind<T>().To(source.Resolve<T>());

    private void DisposeCurrentSessionContainer()
    {
        CurrentSessionContainer?.Dispose();
        CurrentSessionContainer = null;
    }

    #endregion
}
