using Ninject;
using Ninject.Modules;
using RestSharp;

namespace Rinjani
{
    public class CoreModule : NinjectModule
    {
        public override void Load()
        {
            var path = "config.json";
            Kernel.Bind<IConfigStore>().To<JsonConfigStore>().InSingletonScope()
                .WithConstructorArgument("path", path);
            Kernel.Bind<IQuoteAggregator>().To<QuoteAggregator>().InSingletonScope();
            Kernel.Bind<IBalanceService>().To<BalanceService>().InSingletonScope();
            Kernel.Bind<IBrokerAdapterRouter>().To<BrokerAdapterRouter>();
            Kernel.Bind<IArbitrager>().To<Arbitrager>();
            Kernel.Bind<IBrokerAdapter>().To<Zb.BrokerAdapter>();
            Kernel.Bind<IBrokerAdapter>().To<Hpx.BrokerAdapter>();
            Kernel.Bind<IRestClient>().To<RestClient>();
            Kernel.Bind<ITimer>().To<TimerAdapter>();
            Kernel.Bind<IConfigValidator>().To<ConfigValidator>();
        }
    }

    public class NinjectConfig
    {
        private static IKernel _kernel;
        public static IKernel Kernel => _kernel ?? (_kernel = new StandardKernel(new CoreModule()));
    }
}