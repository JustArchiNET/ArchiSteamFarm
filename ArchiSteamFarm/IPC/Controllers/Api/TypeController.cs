//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// |
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// |
// http://www.apache.org/licenses/LICENSE-2.0
// |
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[Route("Api/Type")]
	public sealed class TypeController : ArchiController {
		/// <summary>
		///     Fetches type info of given type.
		/// </summary>
		/// <remarks>
		///     Type info is defined as a representation of given object with its fields and properties being assigned to a string value that defines their type.
		/// </remarks>
		[HttpGet("{type:required}")]
		[ProducesResponseType(typeof(GenericResponse<TypeResponse>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public ActionResult<GenericResponse> TypeGet(string type) {
			if (string.IsNullOrEmpty(type)) {
				throw new ArgumentNullException(nameof(type));
			}

			Type? targetType = WebUtilities.ParseType(type);

			if (targetType == null) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, type)));
			}

			string? baseType = targetType.BaseType?.GetUnifiedName();
			HashSet<string> customAttributes = targetType.CustomAttributes.Select(attribute => attribute.AttributeType.GetUnifiedName()).Where(customAttribute => !string.IsNullOrEmpty(customAttribute)).ToHashSet(StringComparer.Ordinal)!;
			string? underlyingType = null;

			Dictionary<string, string> body = new(StringComparer.Ordinal);

			if (targetType.IsClass) {
				foreach (FieldInfo field in targetType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Where(field => !field.IsPrivate)) {
					JsonPropertyAttribute? jsonProperty = field.GetCustomAttribute<JsonPropertyAttribute>();

					if (jsonProperty != null) {
						string? unifiedName = field.FieldType.GetUnifiedName();

						if (!string.IsNullOrEmpty(unifiedName)) {
							// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
							body[jsonProperty.PropertyName ?? field.Name] = unifiedName!;
						}
					}
				}

				foreach (PropertyInfo property in targetType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Where(property => property.CanRead && (property.GetMethod?.IsPrivate == false))) {
					JsonPropertyAttribute? jsonProperty = property.GetCustomAttribute<JsonPropertyAttribute>();

					if (jsonProperty != null) {
						string? unifiedName = property.PropertyType.GetUnifiedName();

						if (!string.IsNullOrEmpty(unifiedName)) {
							// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
							body[jsonProperty.PropertyName ?? property.Name] = unifiedName!;
						}
					}
				}
			} else if (targetType.IsEnum) {
				Type enumType = Enum.GetUnderlyingType(targetType);
				underlyingType = enumType.GetUnifiedName();

				foreach (object? value in Enum.GetValues(targetType)) {
					string? valueText = value?.ToString();

					if (string.IsNullOrEmpty(valueText)) {
						ASF.ArchiLogger.LogNullError(nameof(valueText));

						return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(valueText))));
					}

					string? valueObjText = Convert.ChangeType(value, enumType, CultureInfo.InvariantCulture)?.ToString();

					if (string.IsNullOrEmpty(valueObjText)) {
						continue;
					}

					// ReSharper disable RedundantSuppressNullableWarningExpression - required for .NET Framework
					body[valueText!] = valueObjText!;

					// ReSharper restore RedundantSuppressNullableWarningExpression - required for .NET Framework
				}
			}

			TypeProperties properties = new(baseType, customAttributes.Count > 0 ? customAttributes : null, underlyingType);

			TypeResponse response = new(body, properties);

			return Ok(new GenericResponse<TypeResponse>(response));
		}
	}
}
