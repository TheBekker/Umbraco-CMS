// Copyright (c) Umbraco.
// See LICENSE for more details.

using System;
using System.Linq;
using NUnit.Framework;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Persistence.Repositories.Implement;
using Umbraco.Cms.Core.Services.Implement;
using Umbraco.Cms.Infrastructure.Services.Notifications;
using Umbraco.Cms.Tests.Common.Builders;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;
using Umbraco.Extensions;

namespace Umbraco.Cms.Tests.Integration.Umbraco.Infrastructure.Services
{
    [TestFixture]
    [UmbracoTest(
        Database = UmbracoTestOptions.Database.NewSchemaPerTest,
        PublishedRepositoryEvents = true,
        WithApplication = true,
        Logger = UmbracoTestOptions.Logger.Console)]
    public class ContentServiceNotificationTests : UmbracoIntegrationTest
    {
        private IContentTypeService ContentTypeService => GetRequiredService<IContentTypeService>();

        private ContentService ContentService => (ContentService)GetRequiredService<IContentService>();

        private ILocalizationService LocalizationService => GetRequiredService<ILocalizationService>();

        private IFileService FileService => GetRequiredService<IFileService>();

        private GlobalSettings _globalSettings;
        private IContentType _contentType;

        [SetUp]
        public void SetupTest()
        {
            ContentRepositoryBase.ThrowOnWarning = true;
            _globalSettings = new GlobalSettings();

            // TODO: remove this once IPublishedSnapShotService has been implemented with nucache.
            global::Umbraco.Cms.Core.Services.Implement.ContentTypeService.ClearScopeEvents();
            CreateTestData();
        }

        protected override void CustomTestSetup(IUmbracoBuilder builder) => builder
            .AddNotificationHandler<SavingNotification<IContent>, ContentNotificationHandler>()
            .AddNotificationHandler<SavedNotification<IContent>, ContentNotificationHandler>()
            .AddNotificationHandler<PublishingNotification<IContent>, ContentNotificationHandler>()
            .AddNotificationHandler<PublishedNotification<IContent>, ContentNotificationHandler>()
            .AddNotificationHandler<UnpublishingNotification<IContent>, ContentNotificationHandler>()
            .AddNotificationHandler<UnpublishedNotification<IContent>, ContentNotificationHandler>();

        private void CreateTestData()
        {
            Template template = TemplateBuilder.CreateTextPageTemplate();
            FileService.SaveTemplate(template); // else, FK violation on contentType!

            _contentType = ContentTypeBuilder.CreateTextPageContentType(defaultTemplateId: template.Id);
            ContentTypeService.Save(_contentType);
        }

        [TearDown]
        public void Teardown() => ContentRepositoryBase.ThrowOnWarning = false;

        [Test]
        public void Saving_Culture()
        {
            LocalizationService.Save(new Language(_globalSettings, "fr-FR"));

            _contentType.Variations = ContentVariation.Culture;
            foreach (IPropertyType propertyType in _contentType.PropertyTypes)
            {
                propertyType.Variations = ContentVariation.Culture;
            }

            ContentTypeService.Save(_contentType);

            IContent document = new Content("content", -1, _contentType);
            document.SetCultureName("hello", "en-US");
            document.SetCultureName("bonjour", "fr-FR");
            ContentService.Save(document);

            // re-get - dirty properties need resetting
            document = ContentService.GetById(document.Id);

            // properties: title, bodyText, keywords, description
            document.SetValue("title", "title-en", "en-US");

            var savingWasCalled = false;
            var savedWasCalled = false;

            ContentNotificationHandler.SavingContent = notification =>
            {
                IContent saved = notification.SavedEntities.First();

                Assert.AreSame(document, saved);

                Assert.IsTrue(notification.IsSavingCulture(saved, "en-US"));
                Assert.IsFalse(notification.IsSavingCulture(saved, "fr-FR"));

                savingWasCalled = true;
            };

            ContentNotificationHandler.SavedContent = notification =>
            {
                IContent saved = notification.SavedEntities.First();

                Assert.AreSame(document, saved);

                Assert.IsTrue(notification.HasSavedCulture(saved, "en-US"));
                Assert.IsFalse(notification.HasSavedCulture(saved, "fr-FR"));

                savedWasCalled = true;
            };

            try
            {
                ContentService.Save(document);
                Assert.IsTrue(savingWasCalled);
                Assert.IsTrue(savedWasCalled);
            }
            finally
            {
                ContentNotificationHandler.SavingContent = null;
                ContentNotificationHandler.SavedContent = null;
            }
        }

        [Test]
        public void Saving_Set_Value()
        {
            IContent document = new Content("content", -1, _contentType);

            var savingWasCalled = false;
            var savedWasCalled = false;

            ContentNotificationHandler.SavingContent = notification =>
            {
                IContent saved = notification.SavedEntities.First();

                Assert.IsTrue(document.GetValue<string>("title").IsNullOrWhiteSpace());

                saved.SetValue("title", "title");

                savingWasCalled = true;
            };

            ContentNotificationHandler.SavedContent = notification =>
            {
                IContent saved = notification.SavedEntities.First();

                Assert.AreSame("title", document.GetValue<string>("title"));

                // we're only dealing with invariant here
                IPropertyValue propValue = saved.Properties["title"].Values.First(x => x.Culture == null && x.Segment == null);

                Assert.AreEqual("title", propValue.EditedValue);
                Assert.IsNull(propValue.PublishedValue);

                savedWasCalled = true;
            };

            try
            {
                ContentService.Save(document);
                Assert.IsTrue(savingWasCalled);
                Assert.IsTrue(savedWasCalled);
            }
            finally
            {
                ContentNotificationHandler.SavingContent = null;
                ContentNotificationHandler.SavedContent = null;
            }
        }

        [Test]
        public void Publishing_Culture()
        {
            LocalizationService.Save(new Language(_globalSettings, "fr-FR"));

            _contentType.Variations = ContentVariation.Culture;
            foreach (IPropertyType propertyType in _contentType.PropertyTypes)
            {
                propertyType.Variations = ContentVariation.Culture;
            }

            ContentTypeService.Save(_contentType);

            IContent document = new Content("content", -1, _contentType);
            document.SetCultureName("hello", "en-US");
            document.SetCultureName("bonjour", "fr-FR");
            ContentService.Save(document);

            Assert.IsFalse(document.IsCulturePublished("fr-FR"));
            Assert.IsFalse(document.IsCulturePublished("en-US"));

            // re-get - dirty properties need resetting
            document = ContentService.GetById(document.Id);

            var publishingWasCalled = false;
            var publishedWasCalled = false;

            ContentNotificationHandler.PublishingContent += notification =>
            {
                IContent publishing = notification.PublishedEntities.First();

                Assert.AreSame(document, publishing);

                Assert.IsFalse(notification.IsPublishingCulture(publishing, "en-US"));
                Assert.IsTrue(notification.IsPublishingCulture(publishing, "fr-FR"));

                publishingWasCalled = true;
            };

            ContentNotificationHandler.PublishedContent += notification =>
            {
                IContent published = notification.PublishedEntities.First();

                Assert.AreSame(document, published);

                Assert.IsFalse(notification.HasPublishedCulture(published, "en-US"));
                Assert.IsTrue(notification.HasPublishedCulture(published, "fr-FR"));

                publishedWasCalled = true;
            };

            try
            {
                ContentService.SaveAndPublish(document, "fr-FR");
                Assert.IsTrue(publishingWasCalled);
                Assert.IsTrue(publishedWasCalled);
            }
            finally
            {
                ContentNotificationHandler.PublishingContent = null;
                ContentNotificationHandler.PublishedContent = null;
            }

            document = ContentService.GetById(document.Id);

            // ensure it works and does not throw
            Assert.IsTrue(document.IsCulturePublished("fr-FR"));
            Assert.IsFalse(document.IsCulturePublished("en-US"));
        }

        [Test]
        public void Publishing_Set_Value()
        {
            IContent document = new Content("content", -1, _contentType);

            var savingWasCalled = false;
            var savedWasCalled = false;

            ContentNotificationHandler.SavingContent = notification =>
            {
                IContent saved = notification.SavedEntities.First();

                Assert.IsTrue(document.GetValue<string>("title").IsNullOrWhiteSpace());

                saved.SetValue("title", "title");

                savingWasCalled = true;
            };

            ContentNotificationHandler.SavedContent = notification =>
            {
                IContent saved = notification.SavedEntities.First();

                Assert.AreSame("title", document.GetValue<string>("title"));

                // We're only dealing with invariant here.
                IPropertyValue propValue = saved.Properties["title"].Values.First(x => x.Culture == null && x.Segment == null);

                Assert.AreEqual("title", propValue.EditedValue);
                Assert.AreEqual("title", propValue.PublishedValue);

                savedWasCalled = true;
            };

            try
            {
                ContentService.SaveAndPublish(document);
                Assert.IsTrue(savingWasCalled);
                Assert.IsTrue(savedWasCalled);
            }
            finally
            {
                ContentNotificationHandler.SavingContent = null;
                ContentNotificationHandler.SavedContent = null;
            }
        }

        [Test]
        public void Publishing_Set_Mandatory_Value()
        {
            IPropertyType titleProperty = _contentType.PropertyTypes.First(x => x.Alias == "title");
            titleProperty.Mandatory = true; // make this required!
            ContentTypeService.Save(_contentType);

            IContent document = new Content("content", -1, _contentType);

            PublishResult result = ContentService.SaveAndPublish(document);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("title", result.InvalidProperties.First().Alias);

            // when a service operation fails, the object is dirty and should not be re-used,
            // re-create it
            document = new Content("content", -1, _contentType);

            var savingWasCalled = false;

            ContentNotificationHandler.SavingContent = notification =>
            {
                IContent saved = notification.SavedEntities.First();

                Assert.IsTrue(document.GetValue<string>("title").IsNullOrWhiteSpace());

                saved.SetValue("title", "title");

                savingWasCalled = true;
            };

            try
            {
                result = ContentService.SaveAndPublish(document);
                Assert.IsTrue(result.Success); // will succeed now because we were able to specify the required value in the Saving event
                Assert.IsTrue(savingWasCalled);
            }
            finally
            {
                ContentNotificationHandler.SavingContent = null;
            }
        }

        [Test]
        public void Unpublishing_Culture()
        {
            LocalizationService.Save(new Language(_globalSettings, "fr-FR"));

            _contentType.Variations = ContentVariation.Culture;
            foreach (IPropertyType propertyType in _contentType.PropertyTypes)
            {
                propertyType.Variations = ContentVariation.Culture;
            }

            ContentTypeService.Save(_contentType);

            var contentService = (ContentService)ContentService;

            IContent document = new Content("content", -1, _contentType);
            document.SetCultureName("hello", "en-US");
            document.SetCultureName("bonjour", "fr-FR");
            contentService.SaveAndPublish(document);

            Assert.IsTrue(document.IsCulturePublished("fr-FR"));
            Assert.IsTrue(document.IsCulturePublished("en-US"));

            // re-get - dirty properties need resetting
            document = contentService.GetById(document.Id);

            document.UnpublishCulture("fr-FR");

            var unpublishingWasCalled = false;
            var unpublishedWasCalled = false;

            ContentNotificationHandler.UnpublishingContent += notification =>
            {
                IContent unpublished = notification.UnpublishedEntities.First();

                Assert.AreSame(document, unpublished);

                Assert.IsFalse(notification.IsUnpublishingCulture(unpublished, "en-US"));
                Assert.IsTrue(notification.IsUnpublishingCulture(unpublished, "fr-FR"));

                unpublishingWasCalled = true;
            };

            ContentNotificationHandler.UnpublishedContent += notification =>
            {
                IContent unpublished = notification.UnpublishedEntities.First();

                Assert.AreSame(document, unpublished);

                Assert.IsFalse(notification.HasUnpublishedCulture(unpublished, "en-US"));
                Assert.IsTrue(notification.HasUnpublishedCulture(unpublished, "fr-FR"));

                unpublishedWasCalled = true;
            };

            try
            {
                contentService.CommitDocumentChanges(document);
                Assert.IsTrue(unpublishingWasCalled);
                Assert.IsTrue(unpublishedWasCalled);
            }
            finally
            {
                ContentNotificationHandler.UnpublishingContent = null;
                ContentNotificationHandler.UnpublishedContent = null;
            }

            document = contentService.GetById(document.Id);

            Assert.IsFalse(document.IsCulturePublished("fr-FR"));
            Assert.IsTrue(document.IsCulturePublished("en-US"));
        }

        public class ContentNotificationHandler :
            INotificationHandler<SavingNotification<IContent>>,
            INotificationHandler<SavedNotification<IContent>>,
            INotificationHandler<PublishingNotification<IContent>>,
            INotificationHandler<PublishedNotification<IContent>>,
            INotificationHandler<UnpublishingNotification<IContent>>,
            INotificationHandler<UnpublishedNotification<IContent>>
        {
            public void Handle(SavingNotification<IContent> notification) => SavingContent?.Invoke(notification);

            public void Handle(SavedNotification<IContent> notification) => SavedContent?.Invoke(notification);

            public void Handle(PublishingNotification<IContent> notification) => PublishingContent?.Invoke(notification);

            public void Handle(PublishedNotification<IContent> notification) => PublishedContent?.Invoke(notification);

            public void Handle(UnpublishingNotification<IContent> notification) => UnpublishingContent?.Invoke(notification);

            public void Handle(UnpublishedNotification<IContent> notification) => UnpublishedContent?.Invoke(notification);

            public static Action<SavingNotification<IContent>> SavingContent { get; set; }

            public static Action<SavedNotification<IContent>> SavedContent { get; set; }

            public static Action<PublishingNotification<IContent>> PublishingContent { get; set; }

            public static Action<PublishedNotification<IContent>> PublishedContent { get; set; }

            public static Action<UnpublishingNotification<IContent>> UnpublishingContent { get; set; }

            public static Action<UnpublishedNotification<IContent>> UnpublishedContent { get; set; }
        }
    }
}
