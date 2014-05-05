﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Web.Http;
using FluentSecurity.Caching;
using FluentSecurity.Configuration;
using FluentSecurity.Core;
using FluentSecurity.Diagnostics;
using FluentSecurity.Internals;
using FluentSecurity.WebApi.Configuration;
using FluentSecurity.WebApi.Scanning;
using FluentSecurity.WebApi.Scanning.TypeScanners;

namespace FluentSecurity.WebApi
{
	public abstract class WebApiConfigurationExpression : ConfigurationExpressionBase<WebApiSecurityRuntime, AdvancedConfiguration>
	{
		private Func<IControllerNameResolver> _controllerNameResolver;
		private Func<IActionNameResolver> _actionNameResolver;
		private Func<IActionResolver> _actionResolver;

		internal void Initialize(WebApiSecurityRuntime runtime)
		{
			Initialize(runtime, new AdvancedConfiguration(runtime));

			_controllerNameResolver = runtime.Container.Resolve<IControllerNameResolver>;
			_actionNameResolver = runtime.Container.Resolve<IActionNameResolver>;
			_actionResolver = runtime.Container.Resolve<IActionResolver>;
		}

		public IPolicyContainerConfiguration For<TController>(Expression<Func<TController, object>> actionExpression) where TController : ApiController
		{
			var controllerName = _controllerNameResolver().Resolve(typeof(TController));
			var actionName = _actionNameResolver().Resolve(actionExpression);

			return AddPolicyContainerFor(controllerName, actionName);
		}

		public IPolicyContainerConfiguration For<TController>(Expression<Action<TController>> actionExpression) where TController : ApiController
		{
			var controllerName = _controllerNameResolver().Resolve(typeof(TController));
			var actionName = _actionNameResolver().Resolve(actionExpression);

			return AddPolicyContainerFor(controllerName, actionName);
		}

		public IPolicyContainerConfiguration For<TController>() where TController : ApiController
		{
			var controllerType = typeof(TController);
			var controllerTypes = new[] { controllerType };

			return CreateConventionPolicyContainerFor(controllerTypes, defaultCacheLevel: By.Controller);
		}

		public virtual IPolicyContainerConfiguration ForAllControllers()
		{
			var assemblyScanner = new WebApiAssemblyScanner();
			assemblyScanner.TheCallingAssembly();
			assemblyScanner.With<WebApiControllerTypeScanner>();
			var controllerTypes = assemblyScanner.Scan();

			return CreateConventionPolicyContainerFor(controllerTypes);
		}

		public void Scan(Action<WebApiProfileScanner> scan)
		{
			Publish.ConfigurationEvent(() => "Scanning for profiles");
			var profileScanner = new WebApiProfileScanner();
			scan.Invoke(profileScanner);
			var profiles = profileScanner.Scan().ToList();
			profiles.ForEach(ApplyProfile);
		}

		public void ApplyProfile<TSecurityProfile>() where TSecurityProfile : WebApiSecurityProfile, new()
		{
			var profile = new TSecurityProfile();
			Publish.ConfigurationEvent(() => "Applying profile {0}.".FormatWith(profile.GetType().FullName));
			Runtime.ApplyConfiguration(profile);
		}

		private void ApplyProfile(Type profileType)
		{
			var profile = Activator.CreateInstance(profileType) as WebApiSecurityProfile;
			if (profile != null)
			{
				Publish.ConfigurationEvent(() => "Applying profile {0}.".FormatWith(profile.GetType().FullName));
				Runtime.ApplyConfiguration(profile);
			}
		}

		internal IPolicyContainerConfiguration CreateConventionPolicyContainerFor(IEnumerable<Type> controllerTypes, Func<ControllerActionInfo, bool> actionFilter = null, By defaultCacheLevel = By.Policy)
		{
			var policyContainers = new List<IPolicyContainerConfiguration>();
			foreach (var controllerType in controllerTypes)
			{
				var controllerName = _controllerNameResolver().Resolve(controllerType);
				var actionMethods = _actionResolver().ActionMethods(controllerType, actionFilter);

				policyContainers.AddRange(
					actionMethods.Select(actionMethod => AddPolicyContainerFor(controllerName, _actionNameResolver().Resolve(actionMethod)))
					);
			}

			var policyContainer = new ConventionPolicyContainer(policyContainers, defaultCacheLevel);
			policyContainer.SetTypeFactory(Runtime.TypeFactory);
			return policyContainer;
		}

		private IPolicyContainerConfiguration AddPolicyContainerFor(string controllerName, string actionName)
		{
			var policyContainer = new PolicyContainer(controllerName, actionName, PolicyAppender);
			policyContainer.SetTypeFactory(Runtime.TypeFactory);
			return Runtime.AddPolicyContainer(policyContainer);
		}
	}
}