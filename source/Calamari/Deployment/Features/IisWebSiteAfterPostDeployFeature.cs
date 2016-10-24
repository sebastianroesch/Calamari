﻿using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Calamari.Commands.Support;
using Calamari.Integration.Certificates;
using Calamari.Integration.Iis;
using Octostache;

namespace Calamari.Deployment.Features
{
    public class IisWebSiteAfterPostDeployFeature : IisWebSiteFeature
    {
        public override string DeploymentStage => DeploymentStages.AfterPostDeploy; 

        public override void Execute(RunningDeployment deployment)
        {
            var variables = deployment.Variables;

            if (variables.GetFlag(SpecialVariables.Action.IisWebSite.DeployAsWebSite, false))
            {
                // For any bindings using certificate variables, the application pool account
                // must have access to the private-key. 
                EnsureApplicationPoolHasCertificatePrivateKeyAccess(variables);
            }
        }

        static void EnsureApplicationPoolHasCertificatePrivateKeyAccess(VariableDictionary variables)
        {
            foreach (var binding in GetBindings(variables))
            {
                string certificateVariable = binding.certificateVariable;

                if (string.IsNullOrWhiteSpace(certificateVariable))
                    continue;

                var thumbprint = variables.Get($"{certificateVariable}.{SpecialVariables.Certificate.Properties.Thumbprint}");
                var privateKeyAccess = CreatePrivateKeyAccessForApplicationPoolAccount(variables);

                // The store-name variable was set by IisWebSiteBeforePostDeploy
                var storeName = variables.Get(SpecialVariables.Action.IisWebSite.Output.CertificateStoreName);
                WindowsX509CertificateStore.SetPrivateKeySecurity(thumbprint, StoreLocation.LocalMachine, storeName, 
                    new List<PrivateKeyAccessRule> {privateKeyAccess});
            }
            
        }

        static PrivateKeyAccessRule CreatePrivateKeyAccessForApplicationPoolAccount(VariableDictionary variables)
        {
            var applicationPoolIdentityTypeValue = variables.Get(SpecialVariables.Action.IisWebSite.ApplicationPoolIdentityType);

            ApplicationPoolIdentityType appPoolIdentityType;
            if(!Enum.TryParse(applicationPoolIdentityTypeValue, out appPoolIdentityType))
            {
                throw new CommandException($"Unexpected value for '{SpecialVariables.Action.IisWebSite.ApplicationPoolIdentityType}': '{applicationPoolIdentityTypeValue}'");
            }

            return new PrivateKeyAccessRule(
                GetIdentityForApplicationPoolIdentity(appPoolIdentityType, variables), 
                PrivateKeyAccess.FullControl);
        }

        static IdentityReference GetIdentityForApplicationPoolIdentity(ApplicationPoolIdentityType applicationPoolIdentityType,
            VariableDictionary variables)
        {
            switch (applicationPoolIdentityType)
            {
                case ApplicationPoolIdentityType.ApplicationPoolIdentity:
                    return new NTAccount("IIS AppPool\\" + variables.Get(SpecialVariables.Action.IisWebSite.ApplicationPoolName)); 

                case ApplicationPoolIdentityType.LocalService:
                    return new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);

                case ApplicationPoolIdentityType.LocalSystem:
                    return new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);

                case ApplicationPoolIdentityType.NetworkService:
                    return new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null);

                case ApplicationPoolIdentityType.SpecificUser:
                    return new NTAccount(variables.Get(SpecialVariables.Action.IisWebSite.ApplicationPoolUserName));

                default:
                    throw new ArgumentOutOfRangeException(nameof(applicationPoolIdentityType), applicationPoolIdentityType, null);
            }
        }
    }
}