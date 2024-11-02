﻿using FlaUI.Core.AutomationElements;
using FlaUI.Core;

namespace UITest.InformationManager.Views;

public class EmailView(FrameworkAutomationElementBase element) : AutomationElement(element)
{
    public Label TitleLabel => this.Find("TitleLabel").AsLabel();

    public Label FromLabel => this.Find("FromLabel").AsLabel();

    public Label ToLabel => this.Find("ToLabel").AsLabel();

    public Label CCLabel => this.Find("CCLabel").AsLabel();

    public Label BccLabel => this.Find("BccLabel").AsLabel();

    public Label SentLabel => this.Find("SentLabel").AsLabel();
}