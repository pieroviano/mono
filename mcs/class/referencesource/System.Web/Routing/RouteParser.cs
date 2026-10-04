namespace System.Web.Routing {
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;

    internal static class RouteParser {
        private static string GetLiteral(string segmentLiteral) {
            // Scan for errant single { and } and convert double {{ to { and double }} to }

            // First we eliminate all escaped braces and then check if any other braces are remaining
            var newLiteral = segmentLiteral.Replace("{{", "").Replace("}}", "");
            if (newLiteral.Contains("{") || newLiteral.Contains("}")) {
                return null;
            }

            // If it's a valid format, we unescape the braces
            return segmentLiteral.Replace("{{", "{").Replace("}}", "}");
        }

        private static int IndexOfFirstOpenParameter(string segment, int startIndex) {
            // Find the first unescaped open brace
            while (true) {
                startIndex = segment.IndexOf('{', startIndex);
                if (startIndex == -1) {
                    // If there are no more open braces, stop
                    return -1;
                }
                if ((startIndex + 1 == segment.Length) ||
                    ((startIndex + 1 < segment.Length) && (segment[startIndex + 1] != '{'))) {
                    // If we found an open brace that is followed by a non-open brace, it's
                    // a parameter delimiter.
                    // It's also a delimiter if the open brace is the last character - though
                    // it ends up being being called out as invalid later on.
                    return startIndex;
                }
                // Increment by two since we want to skip both the open brace that
                // we're on as well as the subsequent character since we know for
                // sure that it is part of an escape sequence.
                startIndex += 2;
            }
        }

        internal static bool IsSeparator(string s) {
            return String.Equals(s, "/", StringComparison.Ordinal);
        }

        private static bool IsValidParameterName(string parameterName) {
            if (parameterName.Length == 0) {
                return false;
            }

            for (var i = 0; i < parameterName.Length; i++) {
                var c = parameterName[i];
                if (c == '/' || c == '{' || c == '}') {
                    return false;
                }
            }

            return true;
        }

        internal static bool IsInvalidRouteUrl(string routeUrl) {
            return (routeUrl.StartsWith("~", StringComparison.Ordinal) ||
                routeUrl.StartsWith("/", StringComparison.Ordinal) ||
                (routeUrl.IndexOf('?') != -1));
        }

        public static ParsedRoute Parse(string? routeUrl) {
            if (routeUrl == null) {
                routeUrl = String.Empty;
            }

            if (IsInvalidRouteUrl(routeUrl)) {
                throw new ArgumentException(SR.GetString(SR.Route_InvalidRouteUrl), "routeUrl");
            }

            var urlParts = SplitUrlToPathSegmentStrings(routeUrl);
            var ex = ValidateUrlParts(urlParts);
            if (ex != null) {
                throw ex;
            }

            var pathSegments = SplitUrlToPathSegments(urlParts);

            Debug.Assert(urlParts.Count == pathSegments.Count, "The number of string segments should be the same as the number of path segments");

            return new ParsedRoute(pathSegments);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2208:InstantiateArgumentExceptionsCorrectly",
            Justification = "The exceptions are just constructed here, but they are thrown from a method that does have those parameter names.")]
        private static IList<PathSubsegment> ParseUrlSegment(string segment, out Exception exception) {
            var startIndex = 0;

            var pathSubsegments = new List<PathSubsegment>();

            while (startIndex < segment.Length) {
                var nextParameterStart = IndexOfFirstOpenParameter(segment, startIndex);
                if (nextParameterStart == -1) {
                    // If there are no more parameters in the segment, capture the remainder as a literal and stop
                    var lastLiteralPart = GetLiteral(segment.Substring(startIndex));
                    if (lastLiteralPart == null) {
                        exception = new ArgumentException(
                            String.Format(
                                CultureInfo.CurrentUICulture,
                                SR.GetString(SR.Route_MismatchedParameter),
                                segment
                            ),
                            "routeUrl");
                        return null;
                    }
                    if (lastLiteralPart.Length > 0) {
                        pathSubsegments.Add(new LiteralSubsegment(lastLiteralPart));
                    }
                    break;
                }

                var nextParameterEnd = segment.IndexOf('}', nextParameterStart + 1);
                if (nextParameterEnd == -1) {
                    exception = new ArgumentException(
                        String.Format(
                            CultureInfo.CurrentUICulture,
                            SR.GetString(SR.Route_MismatchedParameter),
                            segment
                        ),
                        "routeUrl");
                    return null;
                }

                var literalPart = GetLiteral(segment.Substring(startIndex, nextParameterStart - startIndex));
                if (literalPart == null) {
                    exception = new ArgumentException(
                        String.Format(
                            CultureInfo.CurrentUICulture,
                            SR.GetString(SR.Route_MismatchedParameter),
                            segment
                        ),
                        "routeUrl");
                    return null;
                }
                if (literalPart.Length > 0) {
                    pathSubsegments.Add(new LiteralSubsegment(literalPart));
                }

                var parameterName = segment.Substring(nextParameterStart + 1, nextParameterEnd - nextParameterStart - 1);
                pathSubsegments.Add(new ParameterSubsegment(parameterName));

                startIndex = nextParameterEnd + 1;
            }

            exception = null;
            return pathSubsegments;
        }

        private static IList<PathSegment> SplitUrlToPathSegments(IList<string> urlParts) {
            var pathSegments = new List<PathSegment>();

            foreach (var pathSegment in urlParts) {
                var isCurrentPartSeparator = IsSeparator(pathSegment);
                if (isCurrentPartSeparator) {
                    pathSegments.Add(new SeparatorPathSegment());
                }
                else {
                    Exception exception;
                    var subsegments = ParseUrlSegment(pathSegment, out exception);
                    Debug.Assert(exception == null, "This only gets called after the path has been validated, so there should never be an exception here");
                    pathSegments.Add(new ContentPathSegment(subsegments));
                }
            }
            return pathSegments;
        }

        internal static IList<string> SplitUrlToPathSegmentStrings(string url) {
            var parts = new List<string>();

            if (String.IsNullOrEmpty(url)) {
                return parts;
            }

            var currentIndex = 0;

            // Split the incoming URL into individual parts
            while (currentIndex < url.Length) {
                var indexOfNextSeparator = url.IndexOf('/', currentIndex);
                if (indexOfNextSeparator == -1) {
                    // If there are no more separators, the rest of the string is the last part
                    var finalPart = url.Substring(currentIndex);
                    if (finalPart.Length > 0) {
                        parts.Add(finalPart);
                    }
                    break;
                }

                var nextPart = url.Substring(currentIndex, indexOfNextSeparator - currentIndex);
                if (nextPart.Length > 0) {
                    parts.Add(nextPart);
                }
                Debug.Assert(url[indexOfNextSeparator] == '/', "The separator char itself should always be a '/'.");
                parts.Add("/");
                currentIndex = indexOfNextSeparator + 1;
            }

            return parts;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2208:InstantiateArgumentExceptionsCorrectly",
            Justification = "The exceptions are just constructed here, but they are thrown from a method that does have those parameter names.")]
        private static Exception ValidateUrlParts(IList<string> pathSegments) {
            Debug.Assert(pathSegments != null, "The value should always come from SplitUrl(), and that function should never return null.");

            var usedParameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool? isPreviousPartSeparator = null;

            var foundCatchAllParameter = false;

            foreach (var pathSegment in pathSegments) {
                if (foundCatchAllParameter) {
                    // If we ever start an iteration of the loop and we've already found a
                    // catchall parameter then we have an invalid URL format.
                    return new ArgumentException(
                        String.Format(
                            CultureInfo.CurrentCulture,
                            SR.GetString(SR.Route_CatchAllMustBeLast)
                        ),
                        "routeUrl");
                }

                bool isCurrentPartSeparator;
                if (isPreviousPartSeparator == null) {
                    // Prime the loop with the first value
                    isPreviousPartSeparator = IsSeparator(pathSegment);
                    isCurrentPartSeparator = isPreviousPartSeparator.Value;
                }
                else {
                    isCurrentPartSeparator = IsSeparator(pathSegment);

                    // If both the previous part and the current part are separators, it's invalid
                    if (isCurrentPartSeparator && isPreviousPartSeparator.Value) {
                        return new ArgumentException(SR.GetString(SR.Route_CannotHaveConsecutiveSeparators), "routeUrl");
                    }
                    Debug.Assert(isCurrentPartSeparator != isPreviousPartSeparator.Value, "This assert should only happen if both the current and previous parts are non-separators. This should never happen because consecutive non-separators are always parsed as a single part.");
                    isPreviousPartSeparator = isCurrentPartSeparator;
                }

                // If it's not a separator, parse the segment for parameters and validate it
                if (!isCurrentPartSeparator) {
                    Exception exception;
                    var subsegments = ParseUrlSegment(pathSegment, out exception);
                    if (exception != null) {
                        return exception;
                    }
                    exception = ValidateUrlSegment(subsegments, usedParameterNames, pathSegment);
                    if (exception != null) {
                        return exception;
                    }

                    foundCatchAllParameter = subsegments.Any<PathSubsegment>(seg => (seg is ParameterSubsegment) && (((ParameterSubsegment)seg).IsCatchAll));
                }
            }
            return null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2208:InstantiateArgumentExceptionsCorrectly",
            Justification = "The exceptions are just constructed here, but they are thrown from a method that does have those parameter names.")]
        private static Exception ValidateUrlSegment(IList<PathSubsegment> pathSubsegments, HashSet<string> usedParameterNames, string pathSegment) {
            var segmentContainsCatchAll = false;

            Type previousSegmentType = null;

            foreach (var subsegment in pathSubsegments) {
                if (previousSegmentType != null) {
                    if (previousSegmentType == subsegment.GetType()) {
                        return new ArgumentException(
                            String.Format(
                                CultureInfo.CurrentCulture,
                                SR.GetString(SR.Route_CannotHaveConsecutiveParameters)
                            ),
                            "routeUrl");
                    }
                }
                previousSegmentType = subsegment.GetType();

                LiteralSubsegment literalSubsegment = subsegment as LiteralSubsegment;
                if (literalSubsegment != null) {
                    // Nothing to validate for literals - everything is valid
                }
                else {
                    ParameterSubsegment parameterSubsegment = subsegment as ParameterSubsegment;
                    if (parameterSubsegment != null) {
                        var parameterName = parameterSubsegment.ParameterName;

                        if (parameterSubsegment.IsCatchAll) {
                            segmentContainsCatchAll = true;
                        }

                        // Check for valid characters in the parameter name
                        if (!IsValidParameterName(parameterName)) {
                            return new ArgumentException(
                                String.Format(
                                    CultureInfo.CurrentUICulture,
                                    SR.GetString(SR.Route_InvalidParameterName),
                                    parameterName
                                ),
                                "routeUrl");
                        }

                        if (usedParameterNames.Contains(parameterName)) {
                            return new ArgumentException(
                                String.Format(
                                    CultureInfo.CurrentUICulture,
                                    SR.GetString(SR.Route_RepeatedParameter),
                                    parameterName
                                ),
                                "routeUrl");
                        }
                        else {
                            usedParameterNames.Add(parameterName);
                        }
                    }
                    else {
                        Debug.Fail("Invalid path subsegment type");
                    }
                }
            }

            if (segmentContainsCatchAll && (pathSubsegments.Count != 1)) {
                return new ArgumentException(
                    String.Format(
                        CultureInfo.CurrentCulture,
                        SR.GetString(SR.Route_CannotHaveCatchAllInMultiSegment)
                    ),
                    "routeUrl");
            }

            return null;
        }
    }
}
