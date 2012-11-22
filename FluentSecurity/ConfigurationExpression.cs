using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Web.Mvc;
using FluentSecurity.Caching;
using FluentSecurity.Configuration;
using FluentSecurity.Internals;
using FluentSecurity.Policy.ViolationHandlers.Conventions;
using FluentSecurity.Scanning;
using FluentSecurity.Scanning.TypeScanners;
using FluentSecurity.ServiceLocation;

namespace FluentSecurity
{
	public class ConfigurationExpression
	{
		public AdvancedConfiguration Advanced { get; private set; }

		internal SecurityModel Model { get; private set; }

		private IPolicyAppender PolicyAppender { get; set; }

		public ConfigurationExpression()
		{
			Initialize(new SecurityModel());
		}

		internal void Initialize(SecurityModel model)
		{
			Model = model;
			Advanced = new AdvancedConfiguration(Model);
			PolicyAppender = new DefaultPolicyAppender();
		}

		public IPolicyContainerConfiguration For<TController>(Expression<Func<TController, object>> actionExpression) where TController : Controller
		{
			var controllerName = typeof(TController).GetControllerName();
			var actionName = actionExpression.GetActionName();

			return AddPolicyContainerFor(controllerName, actionName);
		}

		public IPolicyContainerConfiguration For<TController>() where TController : Controller
		{
			var controllerType = typeof(TController);
			var controllerTypes = new[] { controllerType };

			return CreateConventionPolicyContainerFor(controllerTypes, defaultCacheLevel: By.Controller);
		}

		public IPolicyContainerConfiguration ForAllControllers()
		{
			var assemblyScanner = new AssemblyScanner();
			assemblyScanner.TheCallingAssembly();
			assemblyScanner.With<ControllerTypeScanner>();
			var controllerTypes = assemblyScanner.Scan();

			return CreateConventionPolicyContainerFor(controllerTypes);
		}

		public IPolicyContainerConfiguration ForAllControllersInAssembly(params Assembly[] assemblies)
		{
			var assemblyScanner = new AssemblyScanner();
			assemblyScanner.Assemblies(assemblies);
			assemblyScanner.With<ControllerTypeScanner>();
			var controllerTypes = assemblyScanner.Scan();

			return CreateConventionPolicyContainerFor(controllerTypes);
		}

		public IPolicyContainerConfiguration ForAllControllersInAssemblyContainingType<TType>()
		{
			var assembly = typeof (TType).Assembly;
			return ForAllControllersInAssembly(assembly);
		}

		public IPolicyContainerConfiguration ForAllControllersInheriting<TController>(Expression<Func<TController, object>> actionExpression, params Assembly[] assemblies) where TController : Controller
		{
			if (actionExpression == null) throw new ArgumentNullException("actionExpression");
			var actionName = actionExpression.GetActionName();
			return ForAllControllersInheriting<TController>(action => action == actionName, assemblies);
		}

		public IPolicyContainerConfiguration ForAllControllersInheriting<TController>(params Assembly[] assemblies) where TController : Controller
		{
			Func<string, bool> actionFilter = actionName => true;
			return ForAllControllersInheriting<TController>(actionFilter, assemblies);
		}

		private IPolicyContainerConfiguration ForAllControllersInheriting<TController>(Func<string, bool> actionFilter, IEnumerable<Assembly> assemblies) where TController : Controller
		{
			var controllerType = typeof (TController);

			var assembliesToScan = assemblies.ToList();
			if (!assembliesToScan.Any())
				assembliesToScan.Add(controllerType.Assembly);

			var assemblyScanner = new AssemblyScanner();
			assemblyScanner.Assemblies(assembliesToScan);
			assemblyScanner.With(new ControllerTypeScanner(controllerType));
			var controllerTypes = assemblyScanner.Scan();

			Func<ControllerActionInfo, bool> filter = info => actionFilter.Invoke(info.ActionName);

			return CreateConventionPolicyContainerFor(controllerTypes, filter);
		}

		public IPolicyContainerConfiguration ForAllControllersInNamespaceContainingType<TType>()
		{
			var assembly = typeof (TType).Assembly;

			var assemblyScanner = new AssemblyScanner();
			assemblyScanner.Assembly(assembly);
			assemblyScanner.With<ControllerTypeScanner>();
			assemblyScanner.IncludeNamespaceContainingType<TType>();
			var controllerTypes = assemblyScanner.Scan();

			return CreateConventionPolicyContainerFor(controllerTypes);
		}

		public IPolicyContainerConfiguration ForActionsMatching(Func<ControllerActionInfo, bool> actionFilter, params Assembly[] assemblies)
		{
			var assemblyScanner = new AssemblyScanner();
			var assembliesToScan = assemblies.ToList();
			
			if (assembliesToScan.Any()) 
				assemblyScanner.Assemblies(assemblies);
			else
				assemblyScanner.TheCallingAssembly();

			assemblyScanner.With<ControllerTypeScanner>();
			var controllerTypes = assemblyScanner.Scan();

			return CreateConventionPolicyContainerFor(controllerTypes, actionFilter);
		}

		public void Scan(Action<ProfileScanner> scan)
		{
			var profileScanner = new ProfileScanner();
			scan.Invoke(profileScanner);
			var profiles = profileScanner.Scan().ToList();
			profiles.ForEach(ApplyProfile);
		}

		private void ApplyProfile(Type profileType)
		{
			var profile = Activator.CreateInstance(profileType) as SecurityProfile;
			if (profile != null) Model.ApplyConfiguration(profile);
		}

		private IPolicyContainerConfiguration CreateConventionPolicyContainerFor(IEnumerable<Type> controllerTypes, Func<ControllerActionInfo, bool> actionFilter = null, By defaultCacheLevel = By.Policy)
		{
			var policyContainers = new List<IPolicyContainerConfiguration>();
			foreach (var controllerType in controllerTypes)
			{
				var controllerName = controllerType.GetControllerName();
				var actionMethods = controllerType.GetActionMethods(actionFilter);

				policyContainers.AddRange(
					actionMethods.Select(actionMethod => AddPolicyContainerFor(controllerName, actionMethod.GetActionName()))
					);
			}

			return new ConventionPolicyContainer(policyContainers, defaultCacheLevel);
		}

		private PolicyContainer AddPolicyContainerFor(string controllerName, string actionName)
		{
			return Model.AddPolicyContainer(new PolicyContainer(controllerName, actionName, PolicyAppender));
		}

		// TODO: Evaluate the need to move this to RootConfigurationExpression.
		public void GetAuthenticationStatusFrom(Func<bool> authenticationExpression)
		{
			if (authenticationExpression == null)
				throw new ArgumentNullException("authenticationExpression");

			Model.IsAuthenticated = authenticationExpression;
		}

		// TODO: Evaluate the need to move this to RootConfigurationExpression.
		public void GetRolesFrom(Func<IEnumerable<object>> rolesExpression)
		{
			if (rolesExpression == null)
				throw new ArgumentNullException("rolesExpression");

			if (Model.PolicyContainers.Any())
				throw new ConfigurationErrorsException("You must set the rolesfunction before adding policies.");

			Model.Roles = rolesExpression;
		}

		public void SetPolicyAppender(IPolicyAppender policyAppender)
		{
			if (policyAppender == null)
				throw new ArgumentNullException("policyAppender");
			
			PolicyAppender = policyAppender;
		}

		// TODO: Evaluate the need to move this to RootConfigurationExpression.
		public void ResolveServicesUsing(Func<Type, IEnumerable<object>> servicesLocator, Func<Type, object> singleServiceLocator = null)
		{
			if (servicesLocator == null)
				throw new ArgumentNullException("servicesLocator");

			ResolveServicesUsing(new ExternalServiceLocator(servicesLocator, singleServiceLocator));
		}

		// TODO: Evaluate the need to move this to RootConfigurationExpression.
		public void ResolveServicesUsing(ISecurityServiceLocator securityServiceLocator)
		{
			if (securityServiceLocator == null)
				throw new ArgumentNullException("securityServiceLocator");

			Model.ExternalServiceLocator = securityServiceLocator;
		}

		public void DefaultPolicyViolationHandlerIs<TPolicyViolationHandler>() where TPolicyViolationHandler : class, IPolicyViolationHandler
		{
			RemoveDefaultPolicyViolationHandlerConventions();
			Advanced.Conventions(conventions =>
				conventions.Add(new DefaultPolicyViolationHandlerIsOfTypeConvention<TPolicyViolationHandler>())
				);
		}

		public void DefaultPolicyViolationHandlerIs<TPolicyViolationHandler>(Func<TPolicyViolationHandler> policyViolationHandler) where TPolicyViolationHandler : class, IPolicyViolationHandler
		{
			RemoveDefaultPolicyViolationHandlerConventions();
			Advanced.Conventions(conventions =>
				conventions.Add(new DefaultPolicyViolationHandlerIsInstanceConvention<TPolicyViolationHandler>(policyViolationHandler))
				);
		}

		private void RemoveDefaultPolicyViolationHandlerConventions()
		{
			Advanced.Conventions(conventions =>
			{
				conventions.RemoveAll(c => c is FindDefaultPolicyViolationHandlerByNameConvention);
				conventions.RemoveAll(c => c.IsMatchForGenericType(typeof (DefaultPolicyViolationHandlerIsOfTypeConvention<>)));
				conventions.RemoveAll(c => c.IsMatchForGenericType(typeof (DefaultPolicyViolationHandlerIsInstanceConvention<>)));
			});
		}
	}
}