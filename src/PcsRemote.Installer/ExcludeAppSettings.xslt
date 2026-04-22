<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:wix="http://wixtoolset.org/schemas/v4/wxs">

  <!-- Key to identify Components containing appsettings.json (exact match) -->
  <xsl:key name="AppSettingsComponent"
           match="wix:Component[wix:File[@Source='SourceDir\appsettings.json']]"
           use="@Id" />

  <!-- Key to identify Components inside the logs directory -->
  <xsl:key name="LogsComponent"
           match="wix:Directory[@Name='logs']//wix:Component"
           use="@Id" />

  <!-- Identity transform: copy everything by default -->
  <xsl:template match="@*|node()">
    <xsl:copy>
      <xsl:apply-templates select="@*|node()" />
    </xsl:copy>
  </xsl:template>

  <!-- Remove the Component containing appsettings.json
       (hand-authored in Package.wxs with NeverOverwrite) -->
  <xsl:template match="wix:Component[wix:File[@Source='SourceDir\appsettings.json']]" />

  <!-- Remove the corresponding ComponentRef for appsettings.json -->
  <xsl:template match="wix:ComponentRef[key('AppSettingsComponent', @Id)]" />

  <!-- Remove the logs directory and all its children (runtime artifacts) -->
  <xsl:template match="wix:Directory[@Name='logs']" />

  <!-- Remove ComponentRefs for logs directory components -->
  <xsl:template match="wix:ComponentRef[key('LogsComponent', @Id)]" />

</xsl:stylesheet>
