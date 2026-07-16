import re
with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\App.xaml', 'r', encoding='utf-8') as f:
    text = f.read()

new_styles = '''
            <Style TargetType="TextBox">
                <Setter Property="Background" Value="{DynamicResource InputBackgroundBrush}"/>
                <Setter Property="Foreground" Value="{DynamicResource InputTextBrush}"/>
                <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
                <Setter Property="BorderThickness" Value="1"/>
                <Setter Property="Padding" Value="4,2"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="TextBox">
                            <Border Background="{TemplateBinding Background}" 
                                    BorderBrush="{TemplateBinding BorderBrush}" 
                                    BorderThickness="{TemplateBinding BorderThickness}"
                                    CornerRadius="2">
                                <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}"/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsEnabled" Value="False">
                                    <Setter Property="Opacity" Value="0.6"/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            
            <Style TargetType="ComboBox">
                <Setter Property="Background" Value="{DynamicResource InputBackgroundBrush}"/>
                <Setter Property="Foreground" Value="{DynamicResource InputTextBrush}"/>
                <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
                <Setter Property="BorderThickness" Value="1"/>
                <Setter Property="Padding" Value="4,2"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ComboBox">
                            <Border x:Name="border" Background="{TemplateBinding Background}" 
                                    BorderBrush="{TemplateBinding BorderBrush}" 
                                    BorderThickness="{TemplateBinding BorderThickness}"
                                    CornerRadius="2">
                                <Grid>
                                    <ToggleButton x:Name="ToggleButton" BorderThickness="0" 
                                                  Background="Transparent" 
                                                  ClickMode="Press" Focusable="False" 
                                                  IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                                        <ToggleButton.Template>
                                            <ControlTemplate TargetType="ToggleButton">
                                                <Grid>
                                                    <Grid.ColumnDefinitions>
                                                        <ColumnDefinition />
                                                        <ColumnDefinition Width="20" />
                                                    </Grid.ColumnDefinitions>
                                                    <Border Grid.ColumnSpan="2" Background="{TemplateBinding Background}" />
                                                    <Path x:Name="Arrow" Grid.Column="1" Fill="{DynamicResource TextBrush}" HorizontalAlignment="Center" VerticalAlignment="Center" Data="M 0 0 L 4 4 L 8 0 Z" />
                                                </Grid>
                                                <ControlTemplate.Triggers>
                                                    <Trigger Property="IsMouseOver" Value="True">
                                                        <Setter TargetName="Arrow" Property="Fill" Value="{DynamicResource AccentBrush}" />
                                                    </Trigger>
                                                </ControlTemplate.Triggers>
                                            </ControlTemplate>
                                        </ToggleButton.Template>
                                    </ToggleButton>
                                    <ContentPresenter x:Name="ContentSite" IsHitTestVisible="False" 
                                                      Content="{TemplateBinding SelectionBoxItem}" 
                                                      ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" 
                                                      ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" 
                                                      Margin="{TemplateBinding Padding}" VerticalAlignment="Center" HorizontalAlignment="Left" />
                                    <Popup x:Name="Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False" PopupAnimation="Slide">
                                        <Grid x:Name="DropDown" SnapsToDevicePixels="True" MinWidth="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}">
                                            <Border x:Name="DropDownBorder" Background="{DynamicResource PanelBackgroundBrush}" BorderThickness="1" BorderBrush="{DynamicResource BorderBrush}" />
                                            <ScrollViewer Margin="1" SnapsToDevicePixels="True">
                                                <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Contained" />
                                            </ScrollViewer>
                                        </Grid>
                                    </Popup>
                                </Grid>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsEnabled" Value="False">
                                    <Setter Property="Opacity" Value="0.6"/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            
            <Style TargetType="ComboBoxItem">
                <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
                <Setter Property="Padding" Value="4,2"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ComboBoxItem">
                            <Border x:Name="Bd" Background="Transparent" Padding="{TemplateBinding Padding}">
                                <ContentPresenter />
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True">
                                    <Setter TargetName="Bd" Property="Background" Value="{DynamicResource ButtonHoverBrush}" />
                                </Trigger>
                                <Trigger Property="IsSelected" Value="True">
                                    <Setter TargetName="Bd" Property="Background" Value="{DynamicResource AccentBrush}" />
                                    <Setter Property="Foreground" Value="{DynamicResource AccentTextBrush}" />
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
'''
import re
text = re.sub(r'<Style TargetType="TextBox">.*?</Style>', '', text, flags=re.DOTALL)
text = re.sub(r'<Style TargetType="ComboBox">.*?</Style>', '', text, flags=re.DOTALL)

text = text.replace('<Style TargetType="CheckBox">', new_styles + '\n            <Style TargetType="CheckBox">')

with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\App.xaml', 'w', encoding='utf-8') as f:
    f.write(text)
print('App.xaml updated with flat ControlTemplates')
