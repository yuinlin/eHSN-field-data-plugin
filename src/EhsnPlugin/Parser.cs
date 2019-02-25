﻿using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using EhsnPlugin.Mappers;
using EhsnPlugin.Validators;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.Results;

namespace EhsnPlugin
{
    public class Parser
    {
        private readonly IFieldDataResultsAppender _appender;
        private readonly ILog _logger;

        private VersionValidator VersionValidator { get; } = new VersionValidator();
 
        public Parser(IFieldDataResultsAppender appender, ILog logger)
        {
            _appender = appender;
            _logger = logger;
        }

        public EHSN LoadFromStream(Stream stream)
        {
            try
            {
                var cleanedUpXml = GetXmlWithEmptyElementsRemoved(stream);

                var serializer = new XmlSerializer(typeof(EHSN));
                var memoryStream = new MemoryStream((new UTF8Encoding()).GetBytes(cleanedUpXml));              

                return serializer.Deserialize(memoryStream) as EHSN;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private string GetXmlWithEmptyElementsRemoved(Stream stream)
        {
            using (var streamReader = new StreamReader(stream, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
            {
                var originalXml = streamReader.ReadToEnd();
                stream.Position = 0;

                return Regex.Replace(originalXml, @"<[a-zA-Z]\w*\/>", string.Empty);
            }
        }

        public void Parse(EHSN eHsn)
        {
            VersionValidator.ThrowIfUnsupportedVersion(eHsn.version);

            _logger.Info($"Parsing eHSN '{eHsn.version}' from location '{eHsn.GenInfo.station.number}' ({eHsn.GenInfo.station.Value}) collected on {eHsn.GenInfo.date.Value}");

            var locationIdentifier = eHsn.GenInfo.station.number;

            var locationInfo = _appender.GetLocationByIdentifier(locationIdentifier);

            var mapper = new FieldVisitMapper(eHsn, locationInfo, _logger);

            var fieldVisitInfo = AppendMappedFieldVisitInfo(mapper, locationInfo);

            AppendMappedMeasurements(mapper, fieldVisitInfo);
        }

        private FieldVisitInfo AppendMappedFieldVisitInfo(FieldVisitMapper mapper, LocationInfo locationInfo)
        {
            var fieldVisitDetails = mapper.MapFieldVisitDetails();

            _logger.Info($"Successfully parsed one visit '{fieldVisitDetails.FieldVisitPeriod}' for location '{locationInfo.LocationIdentifier}'");

            return _appender.AddFieldVisit(locationInfo, fieldVisitDetails);
        }

        private void AppendMappedMeasurements(FieldVisitMapper mapper, FieldVisitInfo fieldVisitInfo)
        {
            _appender.AddDischargeActivity(fieldVisitInfo, mapper.MapDischargeActivity());

            var readings = mapper.MapReadings();

            foreach (var reading in readings)
            {
                _appender.AddReading(fieldVisitInfo, reading);
            }

            var levelSurvey = mapper.MapLevelSurveyOrNull();
            if(levelSurvey != null)
            {
                _appender.AddLevelSurvey(fieldVisitInfo, levelSurvey);
            }
        }
    }
}
