using Marpara2.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Strings;

namespace Marpara2.Composers;

public class TurkishUrlSegmentProviderComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.UrlSegmentProviders().Insert<TurkishUrlSegmentProvider>();
    }
}
