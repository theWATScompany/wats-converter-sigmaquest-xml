using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Virinco.WATS.Interface;
using Virinco.WATS.Interface.MES.Product;

namespace Virinco.WATS.Converter.SigmaQuest
{
    public class GenericXMLConverter : IReportConverter_v2
    {
        public Dictionary<string, string> ConverterParameters { get; private set; }

        private Regex gradeRegex = new Regex(@"^\w+");

        public GenericXMLConverter()
        {
            ConverterParameters = new Dictionary<string, string>() {
                {"operationTypeCode", "10" }
            };
        }

        public GenericXMLConverter(IDictionary<string, string> args)
        {
            ConverterParameters = (Dictionary<string, string>)args;
        }

        public void CleanUp()
        {
        }

        private double parseStringDouble(string numberString)
        {
            numberString = numberString.Replace(',', '.');

            double number;
            if (Double.TryParse(numberString, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
            else
            {
                throw new Exception("Could not parse result to double: " + numberString);
            }
        }

        public Report ImportReport(TDM api, Stream file)
        {
            api.TestMode = TestModeType.Import;

            var xmlSerializer = new XmlSerializer(typeof(UnitReport));
            var report = (UnitReport)xmlSerializer.Deserialize(new NamespaceIgnorantXmlTextReader(file));

            if (report.Category.Items == null)
                throw new InvalidOperationException("Missing Product.");

            var product = report.Category.Items.OfType<Product>().First();
            var stationProperties = report.Station.Item?.Items?.ToList() ?? new List<object>();

            string @operator = report.Operator.name;
            string pn = product.partno;
            string sn = product.serialno;
            string rev = product.version;
            string process = ConverterParameters["operationTypeCode"];
            string sequence = report.TestRun.name;
            string version = report.softwareversion;

            string stationName = report.Station.name;
            string location = null;
            var start = report.starttime;


            if (version == null && stationProperties != null)
            {
                var value = stationProperties.OfType<ValueString>().FirstOrDefault(v => string.Equals(v.name, "Test Software", StringComparison.OrdinalIgnoreCase));
                if (value != null)
                {
                    version = value.Value;
                    stationProperties.Remove(value);
                }
            }

            if (stationProperties != null)
            {
                var value = stationProperties.OfType<ValueString>().FirstOrDefault(v => string.Equals(v.name, "Location", StringComparison.OrdinalIgnoreCase));
                if (value != null)
                {
                    location = value.Value;
                    stationProperties.Remove(value);
                }
            }

            var uut = api.CreateUUTReport(@operator, pn, rev, sn, process, sequence, version);
            uut.StartDateTimeOffset = start;
            uut.StationName = stationName;

            if (location != null)
                uut.Location = location;

            if (report.endtime_text != null)
                uut.ExecutionTime = (report.endtime - start).TotalSeconds;

            uut.AddMiscUUTInfo("Mode", report.mode);

            if (report.Property != null && report.Property.Items != null)
            {
                foreach (var item in report.Property.Items)
                    AddMiscInfo(item, uut);
            }

            if (stationProperties != null)
            {
                foreach (var item in stationProperties)
                    AddMiscInfo(item, uut);
            }

            bool skipFirst = true;
            AddSubUnits(report.Category, uut, ref skipFirst);

            //uut.Purpose;
            //uut.BatchSerialNumber
            //uut.FixtureId
            //uut.TestSocketIndex

            var rootSeq = uut.GetRootSequenceCall();
            AddSteps(report.TestRun, rootSeq, true);


            uut.Status = GetUUTStatus(report.TestRun.grade);
            Console.WriteLine("returning uut");
            return uut;


        }

        private void AddSubUnits(Category category, UUTReport uut, ref bool skipFirst)
        {
            if (category.Items != null)
            {
                foreach (var property in category.Items.OfType<Property>())
                {
                    foreach (var item in property.Items)
                        AddMiscInfo(item, uut);
                }

                foreach (var product in category.Items.OfType<Product>())
                    AddSubUnits(product, uut, ref skipFirst);

                foreach (var subCategory in category.Items.OfType<Category>())
                    AddSubUnits(subCategory, uut, ref skipFirst);
            }
        }

        private void AddSubUnits(Product product, UUTReport uut, ref bool skipFirst)
        {
            if (skipFirst)
                skipFirst = false;
            else
                uut.AddUUTPartInfo("", product.partno, product.serialno, product.version ?? "");

            if (product.Items != null)
            {
                foreach (var property in product.Items.OfType<Property>())
                {
                    foreach (var item in property.Items)
                        AddMiscInfo(item, uut);
                }

                foreach (var subProduct in product.Items.OfType<Product>())
                    AddSubUnits(subProduct, uut, ref skipFirst);

                foreach (var category in product.Items.OfType<Category>())
                    AddSubUnits(category, uut, ref skipFirst);
            }
        }

        private void AddMiscInfo(object item, UUTReport uut)
        {
            var (key, value) = GetKeyValue(item);
            if (value != null)
            {
                if (value.Length > 100)
                    value = value.Substring(0, 100);

                uut.AddMiscUUTInfo(key, value);
            }
        }

        private void AddSteps(TestRun testRun, SequenceCall seq, bool isRoot)
        {
            Step step = null;
            var errors = new List<string>();
            var subRuns = new List<TestRun>();
            var reportText = new List<string>();
            var status = GetStepStatus(testRun.grade);

            if (testRun.Items != null)
            {
                subRuns = testRun.Items.OfType<TestRun>().ToList();

                var results = testRun.Items.OfType<Result>();
                var setups = testRun.Items.OfType<SetUp>();
                if (setups.Any() && results.Any())
                    throw new InvalidOperationException("TestRun with SetUp and Result.");

                CompOperatorType? comp = null;
                List<ValueAttachment> attachments = null;
                var properties = testRun.Items.OfType<Property>().SelectMany(p => p.Items).ToList();
                if (properties.Any())
                {
                    var comparison = properties.OfType<ValueString>().FirstOrDefault(v => string.Equals(v.name, "Comparison", StringComparison.OrdinalIgnoreCase) || string.Equals(v.name, "COMP", StringComparison.OrdinalIgnoreCase));
                    if (comparison != null)
                    {
                        string value = comparison.Value.Split('(')[0].Trim();
                        switch (value)
                        {
                            case "E":
                            case "EQ":
                                comp = CompOperatorType.EQ;
                                break;
                            case "NE":
                                comp = CompOperatorType.NE;
                                break;
                            case "LE":
                                comp = CompOperatorType.LE;
                                break;
                            case "LT":
                                comp = CompOperatorType.LT;
                                break;
                            case "GE":
                                comp = CompOperatorType.GE;
                                break;
                            case "GT":
                                comp = CompOperatorType.GT;
                                break;
                            case "GELE":
                                comp = CompOperatorType.GELE;
                                break;
                            case "GELT":
                                comp = CompOperatorType.GELT;
                                break;
                            case "GTLE":
                                comp = CompOperatorType.GTLE;
                                break;
                            case "GTLT":
                                comp = CompOperatorType.GTLT;
                                break;
                            case "LEGE":
                                comp = CompOperatorType.LEGE;
                                break;
                            case "LEGT":
                                comp = CompOperatorType.LEGT;
                                break;
                            case "LTGE":
                                comp = CompOperatorType.LTGE;
                                break;
                            case "LTGT":
                                comp = CompOperatorType.LTGT;
                                break;
                        }

                        if (comp != null)
                            properties.Remove(comparison);
                    }

                    var failNotes = properties.OfType<ValueString>().Where(v => string.Equals(v.name, "Fail Notes", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (failNotes.Any())
                    {
                        foreach (var failNote in failNotes)
                        {
                            reportText.Add($"<p>{failNote.Value}</p>");
                            properties.Remove(failNote);
                        }
                    }

                    attachments = properties.OfType<ValueAttachment>().ToList();
                    foreach (var attachment in attachments)
                        properties.Remove(attachment);
                }

                foreach (var item in properties)
                {
                    var (key, value) = GetKeyValue(item);
                    reportText.Add($"<p>{key}={value}</p>");
                }

                foreach (var error in testRun.Items.OfType<Error>())
                    errors.AddRange(error.Message.Select(m => $"<p>{m.name}={m.Value}</p>"));

                if (isRoot)
                    step = seq;
                else if (subRuns.Any())
                {
                    seq = seq.AddSequenceCall(testRun.name);
                    step = seq;
                }

                if (results.Any())
                {
                    var items = new List<object>();
                    foreach (var result in results)
                    {
                        if (result.Items != null)
                            items.AddRange(result.Items);

                        if (result.Property != null)
                            items.AddRange(result.Property.Items);
                    }

                    step = GetStep(items, seq, testRun.name, status, comp, ref reportText);
                }

                if (setups.Any())
                {
                    var items = new List<object>();
                    foreach (var setup in setups)
                    {
                        if (setup.Items != null)
                            items.AddRange(setup.Items);

                        if (setup.Property != null)
                            items.AddRange(setup.Property.Items);
                    }

                    step = GetStep(items, seq, testRun.name, status, comp, ref reportText);

                    if (step != null)
                        step.StepGroup = Virinco.WATS.Interface.StepGroupEnum.Setup;
                }

                if (step != null && attachments != null)
                {
                    foreach (var attachment in attachments)
                    {
                        if (File.Exists(attachment.fqn))
                            step.AttachFile(attachment.fqn, false);
                    }
                }
            }


            if (step == null)
            {
                var pf = seq.AddPassFailStep(testRun.name);
                pf.AddTest(status == StepStatusType.Passed, status);

                step = pf;
            }

            step.Status = GetStepStatus(testRun.grade);

            step.StartDateTime = testRun.starttime.LocalDateTime;
            if (testRun.endtime_text != null)
                step.StepTime = (testRun.endtime - testRun.starttime).TotalSeconds;

            if (reportText.Any())
                step.ReportText = string.Join("", reportText);

            if (errors.Any())
                step.StepErrorMessage = string.Join("", errors);


            if (subRuns.Any())
            {
                foreach (var subRun in subRuns)
                    AddSteps(subRun, seq, false);
            }
        }

        private Step GetStep(List<object> items, SequenceCall seq, string stepName, StepStatusType stepStatus, CompOperatorType? comp, ref List<string> reportText)
        {
            Step result = null;
            if (items.Count > 1)
            {
                var type = items.First().GetType();
                if (!items.All(i => i.GetType() == type))
                {
                    var other = items.First(i => i.GetType() != type);

                    if (other is ValueString ovs)// && ovs.name == "Report Text")
                        reportText.Insert(0, $"<p>{ovs.name}={ovs.Value}</p>");
                    else if (items.First() is ValueString fvs)// && fvs.name == "Report Text")
                    {
                        reportText.Insert(0, $"<p>{fvs.name}={fvs.Value}</p>");
                        items.Remove(fvs);
                    }
                    else
                        throw new InvalidOperationException($"TestRun Result with mixed values, {type.Name} and {other.GetType().Name}.");
                }

                switch (items.First())
                {
                    case ValueDouble vd:
                        {
                            var step = seq.AddNumericLimitStep(stepName);

                            foreach (var value in items.OfType<ValueDouble>())
                            {
                                var status = value.grade != null ? GetStepStatus(value.grade) : stepStatus;

                                Console.WriteLine(stepName + ": " + status);

                                if (value.Value == "null")
                                {
                                    break;
                                }

                                if (string.IsNullOrEmpty(value.uom))
                                {
                                    value.uom = "";
                                }

                                if (value.uslSpecified && value.lslSpecified)
                                    step.AddMultipleTest(parseStringDouble(value.Value), comp.HasValue ? comp.Value : CompOperatorType.GELE, value.lsl, value.usl, value.uom, value.name, status);
                                else if (value.uslSpecified)
                                    step.AddMultipleTest(parseStringDouble(value.Value), comp.HasValue ? comp.Value : CompOperatorType.LE, value.usl, value.uom, value.name, status);
                                else if (value.lslSpecified)
                                    step.AddMultipleTest(parseStringDouble(value.Value), comp.HasValue ? comp.Value : CompOperatorType.GE, value.lsl, value.uom, value.name, status);
                                else
                                    step.AddMultipleTest(parseStringDouble(value.Value), value.uom, value.name, status);
                            }

                            result = step;
                        }
                        break;
                    case ValueInteger vi:
                        {
                            var step = seq.AddNumericLimitStep(stepName);
                            foreach (var value in items.OfType<ValueInteger>())
                            {
                                var status = value.grade != null ? GetStepStatus(value.grade) : stepStatus;

                                if (string.IsNullOrEmpty(value.uom))
                                {
                                    value.uom = "";
                                }

                                if (value.uslSpecified && value.lslSpecified)
                                    step.AddMultipleTest(value.Value, comp.HasValue ? comp.Value : CompOperatorType.GELE, value.lsl, value.usl, value.uom, value.name, status);
                                else if (value.uslSpecified)
                                    step.AddMultipleTest(value.Value, comp.HasValue ? comp.Value : CompOperatorType.LE, value.usl, value.uom, value.name, status);
                                else if (value.lslSpecified)
                                    step.AddMultipleTest(value.Value, comp.HasValue ? comp.Value : CompOperatorType.GE, value.lsl, value.uom, value.name, status);
                                else
                                    step.AddMultipleTest(value.Value, value.uom, value.name, status);
                            }

                            result = step;
                        }
                        break;
                    case ValueString vs:
                        {
                            int tooLongCount = 0;
                            var step = seq.AddStringValueStep(stepName);
                            foreach (var value in items.OfType<ValueString>())
                            {
                                var status = value.grade != null ? GetStepStatus(value.grade) : stepStatus;
                                string limit = !string.IsNullOrEmpty(value.usl) ? value.usl : value.lsl;

                                string s = value.Value;
                                if (s.Length > 100)
                                {
                                    s = s.Substring(0, 100);
                                    reportText.Insert(tooLongCount++, $"Value too long: {value.Value}");
                                }

                                if (string.IsNullOrEmpty(limit))
                                    step.AddMultipleTest(s, value.name, status);
                                else
                                    step.AddMultipleTest(comp.HasValue ? comp.Value : CompOperatorType.IGNORECASE, s, limit, value.name, status);

                            }

                            result = step;
                        }
                        break;
                    //case ValueTimestamp vt:
                    //    key = vt.name;
                    //    value = vt.Value.ToString("yyyy-MMM-dd HH:mm:ss.fff");
                    //    break;
                    case ValueBoolean vb:
                        {
                            var step = seq.AddPassFailStep(stepName);
                            result = step;

                            foreach (var value in items.OfType<ValueBoolean>())
                            {
                                var status = value.grade != null ? GetStepStatus(value.grade) : stepStatus;
                                step.AddMultipleTest(value.Value, value.name, status);
                            }
                        }
                        break;
                        //case ValueRecord vd:
                        //    key = vd.name;
                        //    value = vd.Value.ToString();
                        //    break;
                        //case ValueArray vd:
                        //    key = vd.name;
                        //    value = vd.Value.ToString();
                        //    break;
                        //case ValueAttachment vd:
                        //    key = vd.name;
                        //    value = vd..ToString();
                        //    break;
                }
            }
            else
            {
                var item = items.First();
                switch (item)
                {
                    case ValueDouble vd:
                        {
                            var value = vd;
                            var step = seq.AddNumericLimitStep(stepName);
                            var status = value.grade != null ? GetStepStatus(value.grade) : stepStatus;

                            if (value.Value == "null")
                            {
                                break;
                            }

                            if (string.IsNullOrEmpty(value.uom))
                            {
                                value.uom = "";
                            }

                            if (value.uslSpecified && value.lslSpecified)
                                step.AddTest(parseStringDouble(value.Value), comp.HasValue ? comp.Value : CompOperatorType.GELE, value.lsl, value.usl, value.uom, status);
                            else if (value.uslSpecified)
                                step.AddTest(parseStringDouble(value.Value), comp.HasValue ? comp.Value : CompOperatorType.LE, value.usl, value.uom, status);
                            else if (value.lslSpecified)
                                step.AddTest(parseStringDouble(value.Value), comp.HasValue ? comp.Value : CompOperatorType.GE, value.lsl, value.uom, status);
                            else
                                step.AddTest(parseStringDouble(value.Value), value.uom, status);

                            result = step;
                        }
                        break;
                    case ValueInteger vi:
                        {
                            var value = vi;
                            var step = seq.AddNumericLimitStep(stepName);
                            var status = value.grade != null ? GetStepStatus(value.grade) : stepStatus;

                            if (string.IsNullOrEmpty(value.uom))
                            {
                                value.uom = "";
                            }

                            if (value.uslSpecified && value.lslSpecified)
                                step.AddTest(value.Value, comp.HasValue ? comp.Value : CompOperatorType.GELE, value.lsl, value.usl, value.uom, status);
                            else if (value.uslSpecified)
                                step.AddTest(value.Value, comp.HasValue ? comp.Value : CompOperatorType.LE, value.usl, value.uom, status);
                            else if (value.lslSpecified)
                                step.AddTest(value.Value, comp.HasValue ? comp.Value : CompOperatorType.GE, value.lsl, value.uom, status);
                            else
                                step.AddTest(value.Value, value.uom, status);

                            result = step;
                        }
                        break;
                    case ValueString vs:
                        {
                            var value = vs;
                            var step = seq.AddStringValueStep(stepName);
                            var status = value.grade != null ? GetStepStatus(value.grade) : stepStatus;

                            string s = value.Value;
                            if (s.Length > 100)
                            {
                                s = s.Substring(0, 100);
                                reportText.Insert(0, $"Value too long: {value.Value}");
                            }

                            string limit = !string.IsNullOrEmpty(value.usl) ? value.usl : value.lsl;
                            if (string.IsNullOrEmpty(limit))
                                step.AddTest(s, status);
                            else
                                step.AddTest(comp.HasValue ? comp.Value : CompOperatorType.IGNORECASE, s, limit, status);
                            result = step;
                        }
                        break;
                    //case ValueTimestamp vt:
                    //    key = vt.name;
                    //    value = vt.Value.ToString("yyyy-MMM-dd HH:mm:ss.fff");
                    //    break;
                    case ValueBoolean vb:
                        {
                            var value = vb;
                            var step = seq.AddPassFailStep(stepName);
                            var status = value.grade != null ? GetStepStatus(value.grade) : stepStatus;

                            step.AddTest(value.Value, status);
                            result = step;
                        }
                        break;
                        //case ValueRecord vd:
                        //    key = vd.name;
                        //    value = vd.Value.ToString();
                        //    break;
                        //case ValueArray vd:
                        //    key = vd.name;
                        //    value = vd.Value.ToString();
                        //    break;
                        //case ValueAttachment vd:
                        //    key = vd.name;
                        //    value = vd..ToString();
                        //    break;
                }
            }

            return result;
        }

        private StepStatusType GetStepStatus(string grade)
        {
            var v = gradeRegex.Match(grade).Value;
            switch (v)
            {
                case "PASS":
                case "DONE":
                    return StepStatusType.Passed;
                case "FAIL":
                case "Failed":
                    return StepStatusType.Failed;
                case "INCOMPLETE":
                case "ABORTED":
                    return StepStatusType.Terminated;
                case "SKIPPED":
                    return StepStatusType.Skipped;
                case "PENDING":
                    return StepStatusType.Error;
                default:
                    throw new NotSupportedException($"Unknown grade '{grade}'.");
            }
        }

        private UUTStatusType GetUUTStatus(string grade)
        {
            switch (grade)
            {
                case "PASS":
                case "DONE":
                    return UUTStatusType.Passed;
                case "FAIL":
                    return UUTStatusType.Failed;
                case "INCOMPLETE":
                case "ABORTED":
                    return UUTStatusType.Terminated;
                default:
                    throw new NotSupportedException($"Unknown grade '{grade}'.");
            }
        }

        private (string key, string value) GetKeyValue(object item)
        {
            string key = "";
            string value = "";

            switch (item)
            {
                case ValueDouble vd:
                    key = vd.name;
                    value = vd.Value.ToString();
                    break;
                case ValueInteger vi:
                    key = vi.name;
                    value = vi.Value.ToString();
                    break;
                case ValueString vs:
                    key = vs.name;
                    value = vs.Value;
                    break;
                case ValueTimestamp vt:
                    key = vt.name;
                    value = vt.Value.ToString("yyyy-MMM-dd HH:mm:ss.fff");
                    break;
                case ValueBoolean vb:
                    key = vb.name;
                    value = vb.Value.ToString();
                    break;
                    //case ValueRecord vd:
                    //    key = vd.name;
                    //    value = vd.Value.ToString();
                    //    break;
                    //case ValueArray vd:
                    //    key = vd.name;
                    //    value = vd.Value.ToString();
                    //    break;
                    //case ValueAttachment vd:
                    //    key = vd.name;
                    //    value = vd..ToString();
                    //    break;
            }

            return (key, value);
        }

        public class NamespaceIgnorantXmlTextReader : System.Xml.XmlTextReader
        {
            public NamespaceIgnorantXmlTextReader(Stream input)
                : base(input) { }

            public override string NamespaceURI
            {
                get { return ""; }
            }
        }
    }
}
